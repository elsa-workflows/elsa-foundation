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
    public async ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ClaimsPrincipal actor, ConnectionRecord? proposed,
        CancellationToken cancellationToken = default)
    {
        var tenant = options.Value.TenantId;
        if (!subjects.TryResolve(actor, out var subject) || subject.TenantId != tenant || subject.CanonicalUserId is null) return null;
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        return await IdentitySnapshot.CaptureAsync(services, tenant, subject.CanonicalUserId, proposed?.Admission.DefaultRoleIds ?? [], async () =>
        {
            if (!await AuthorizeAsync(actor, cancellationToken) || proposed is not null && !await AuthorizePolicyAsync(actor, proposed, cancellationToken)) return false;
            if (services.GetService<ICanonicalUserSignInService>() is not { } authority ||
                !await authority.CanSignInAsync(new(tenant, subject.CanonicalUserId), cancellationToken: cancellationToken)) return false;
            var permissions = services.GetService<ICurrentSubjectPermissionAuthorizer>();
            if (permissions is null || (await permissions.AuthorizeAsync(new(tenant, subject.CanonicalUserId,
                    DefaultIdentityPermissionKeys.IdentityProvidersManage), cancellationToken)).Status != CurrentSubjectPermissionStatus.Granted) return false;
            if (proposed is null) return true;
            var roles = services.GetRequiredService<IRoleStore>();
            foreach (var id in proposed.Admission.DefaultRoleIds.Distinct(StringComparer.Ordinal))
            {
                var role = await roles.FindAsync(tenant, id, cancellationToken);
                if (role is null) return false;
                foreach (var permission in role.Permissions)
                    if ((await permissions.AuthorizeAsync(new(tenant, subject.CanonicalUserId, permission, role), cancellationToken)).Status != CurrentSubjectPermissionStatus.Granted)
                        return false;
            }
            return true;
        }, cancellationToken);
    }

    public async ValueTask<bool> AuthorizeAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
        subjects.TryResolve(actor, out var subject) && subject.TenantId == options.Value.TenantId &&
        (await authorization.AuthorizeAsync(new(actor, DefaultIdentityPermissionKeys.IdentityProvidersManage, options.Value.TenantId), cancellationToken)).Succeeded;

    public async ValueTask<bool> AuthorizePolicyAsync(ClaimsPrincipal actor, ConnectionRecord proposed, CancellationToken cancellationToken = default)
    {
        var tenant = options.Value.TenantId;
        if (!await AuthorizeAsync(actor, cancellationToken)) return false;
        var roleIds = proposed.Admission.DefaultRoleIds.Distinct(StringComparer.Ordinal).ToArray();
        if (roleIds.Length == 0) return true;
        // Delegation uses Elsa role definitions. Coordinated writes separately capture their live revisions.
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
