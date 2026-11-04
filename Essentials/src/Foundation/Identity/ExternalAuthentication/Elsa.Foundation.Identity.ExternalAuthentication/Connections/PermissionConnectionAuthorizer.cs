using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

internal sealed class PermissionConnectionAuthorizer(
    ITokenSubjectResolver subjects,
    IPermissionAuthorizationService authorization,
    IServiceScopeFactory scopes,
    IOptions<ExternalAuthenticationOptions> options) : IConnectionWriteAuthorizer
{
    public async ValueTask<bool> AuthorizeAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
        subjects.TryResolve(actor, out var subject) && subject.TenantId == options.Value.TenantId &&
        (await authorization.AuthorizeAsync(new(actor, DefaultIdentityPermissionKeys.IdentityProvidersManage, options.Value.TenantId), cancellationToken)).Succeeded;

    public async ValueTask<bool> AuthorizePolicyAsync(ClaimsPrincipal actor, ConnectionRecord proposed, CancellationToken cancellationToken = default)
    {
        var tenant = options.Value.TenantId;
        if (!await AuthorizeAsync(actor, cancellationToken)) return false;
        var roleIds = proposed.Admission.DefaultRoleIds.Distinct(StringComparer.Ordinal).ToArray();
        if (roleIds.Length == 0) return true;
        // A catalog with no role assignments does not require a local IAM store. External-owned hosts may
        // supply their own role read adapter and authorization service through the existing contracts.
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var roles = scope.ServiceProvider.GetService<IRoleStore>();
        if (roles is null) return false;
        foreach (var roleId in roleIds)
        {
            if (options.Value.AllowedRoleIds.Count != 0 && !options.Value.AllowedRoleIds.Contains(roleId, StringComparer.Ordinal)) return false;
            var role = await roles.FindAsync(tenant, roleId, cancellationToken);
            if (role is null) return false;
            foreach (var permission in role.Permissions)
                if (!(await authorization.AuthorizeAsync(new(actor, permission, tenant, role), cancellationToken)).Succeeded)
                    return false;
        }
        return true;
    }
}
