using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Dashboard.Api;

internal static class DashboardApiActor
{
    internal static ValueTask<DashboardActor> CurrentAsync(HttpContext context)
    {
        var sources = context.RequestServices.GetServices<IDashboardActorContext>().Take(2).ToArray();
        return sources.Length == 1 ? sources[0].GetAsync(context.RequestAborted) :
            ValueTask.FromException<DashboardActor>(new DashboardResourceException("unavailable", "Exactly one current Dashboard identity source is required."));
    }

    public static async ValueTask<DashboardActor> ResolveAsync(HttpContext context)
    {
        var validator = context.RequestServices.GetRequiredService<NormalizedPrincipalValidator>();
        if (!validator.TryGetNormalizedPrincipal(context.User, out var principal))
            throw new DashboardResourceException("unauthenticated", "A normalized signed-in session is required.");
        var userId = Unique(principal, IdentityClaimTypes.UserId);
        var tenant = Unique(principal, IdentityClaimTypes.TenantId);
        if (userId is null || tenant is null)
            throw new DashboardResourceException("denied", "Dashboards require a canonical user and tenant mapping from the identity provider.");
        var roles = new HashSet<string>(StringComparer.Ordinal);
        if (context.RequestServices.GetService<IUserStore>() is { } users)
        {
            var user = await users.FindAsync(tenant, userId, context.RequestAborted);
            if (user is null || user.Id != userId || user.TenantId != tenant || user.Status != UserStatus.Active)
                throw new DashboardResourceException("denied", "The current user is unavailable or inactive.");
            var roleIds = new HashSet<string>(user.RoleIds, StringComparer.Ordinal);
            if (context.RequestServices.GetService<ITenantMembershipStore>() is { } memberships &&
                await memberships.FindAsync(tenant, userId, context.RequestAborted) is { Status: TenantMembershipStatus.Active } membership &&
                membership.TenantId == tenant && membership.UserId == userId)
                roleIds.UnionWith(membership.RoleIds);
            if (context.RequestServices.GetService<IRoleStore>() is { } roleStore)
                foreach (var roleId in roleIds)
                {
                    var role = await roleStore.FindAsync(tenant, roleId, context.RequestAborted);
                    if (role?.Id == roleId && role.TenantId == tenant) roles.Add(roleId);
                }
        }
        var authority = context.RequestServices.GetRequiredService<IPermissionAuthorizationService>();
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in DashboardResourcePermissions.All.Concat(new[] { "identity/users:read", "identity/roles:read" }))
            if ((await authority.AuthorizeAsync(new(principal, permission, tenant), context.RequestAborted)).Succeeded)
                permissions.Add(permission);
        var subjects = principal.FindAll(ClaimTypes.NameIdentifier).Concat(principal.FindAll("sub"))
            .Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        return new(tenant, userId, roles, permissions) {
            AuthenticationSubjectId = subjects.Length == 1 && !string.IsNullOrWhiteSpace(subjects[0]) ? subjects[0] : null };
    }

    private static string? Unique(ClaimsPrincipal principal, string type)
    {
        var claims = principal.FindAll(type).Select(claim => claim.Value).ToArray();
        return claims.Length == 1 && !string.IsNullOrWhiteSpace(claims[0]) && claims[0].Length <= 256 ? claims[0] : null;
    }
}

internal sealed class HttpDashboardActorContext(IHttpContextAccessor contexts) : IDashboardActorContext
{
    public ValueTask<DashboardActor> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return contexts.HttpContext is { } context ? DashboardApiActor.ResolveAsync(context) :
            ValueTask.FromException<DashboardActor>(new DashboardResourceException("unavailable", "No current authenticated dashboard request is available."));
    }
}

public sealed class DashboardResourcePermissionContributor : IPermissionContributor
{
    public string OwnerId => "elsa.studio.dashboard";
    public IEnumerable<Permission> Contribute() => DashboardResourcePermissions.All.Select(key =>
        new Permission(key, key, "Studio Dashboards", "Controls named dashboard and question resource operations; resource ownership and source-data access are evaluated independently."));
}

public static class DashboardResourcePermissions
{
    public static IReadOnlyList<string> All { get; } = new[] { "questions", "dashboards" }
        .SelectMany(resource => new[] { "read", "create", "edit", "share", "delete" }.Select(verb => $"studio/{resource}:{verb}")).ToArray();
}
