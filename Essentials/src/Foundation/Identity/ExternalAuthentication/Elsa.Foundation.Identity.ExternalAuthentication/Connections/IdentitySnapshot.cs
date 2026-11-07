using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

/// <summary>Captures authority roots around the real permission/sign-in checks; commit must compare every root.</summary>
internal static class IdentitySnapshot
{
    public static async ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureAsync(IServiceProvider services, string tenant,
        string userId, IEnumerable<string> additionalRoles, Func<ValueTask<bool>> check, CancellationToken token)
    {
        var users = services.GetService<IRevisionAwareUserStore>();
        var roles = services.GetService<IRevisionAwareRoleStore>();
        var membership = services.GetService<ITenantMembershipStore>();
        if (users is null || roles is null || membership is null) return null;
        var userGuard = await users.FindRevisionGuardAsync(tenant, userId, token);
        if (userGuard is null) return null;
        var user = await services.GetRequiredService<IUserStore>().FindAsync(tenant, userId, token);
        if (user is null) return null;
        var member = await membership.FindAsync(tenant, user.Id, token);
        var roleIds = user.RoleIds.Concat(member?.RoleIds ?? new HashSet<string>()).Concat(additionalRoles)
            .Distinct(StringComparer.Ordinal).Take(513).ToArray();
        if (roleIds.Length > 512) return null;
        var guards = new List<IamRevisionGuard> { userGuard };
        foreach (var roleId in roleIds)
        {
            var guard = await roles.FindRevisionGuardAsync(tenant, roleId, token);
            if (guard is null) return null;
            guards.Add(guard);
        }
        if (!await check()) return null;
        foreach (var guard in guards)
        {
            var current = guard.Kind == IamRevisionGuardKind.User
                ? await users.FindRevisionGuardAsync(guard.TenantId, guard.Id, token)
                : await roles.FindRevisionGuardAsync(guard.TenantId, guard.Id, token);
            if (current?.Revision != guard.Revision) return null;
        }
        return Merge([], guards);
    }

    public static IReadOnlyList<IamRevisionGuard>? Merge(IEnumerable<IamRevisionGuard> first, IEnumerable<IamRevisionGuard> second)
    {
        var result = new Dictionary<(IamRevisionGuardKind, string, string), IamRevisionGuard>();
        foreach (var guard in first.Concat(second))
        {
            var key = (guard.Kind, guard.TenantId, guard.Id);
            if (result.TryGetValue(key, out var previous) && previous.Revision != guard.Revision) return null;
            result[key] = guard;
            if (result.Count > 513) return null;
        }
        return result.Values.ToArray();
    }
}
