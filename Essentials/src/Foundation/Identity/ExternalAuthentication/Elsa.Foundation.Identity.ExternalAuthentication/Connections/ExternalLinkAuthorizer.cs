using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

internal sealed class ExternalLinkAuthorizer(IServiceScopeFactory scopes, IOptions<ExternalAuthenticationOptions> options) : IExternalLinkAuthorizer
{
    public async ValueTask<ExternalLinkAuthorization?> CaptureAsync(ClaimsPrincipal actor, string userId, string? expectedUserRevision = null,
        CancellationToken cancellationToken = default)
    {
        var tenant = options.Value.TenantId;
        await using var scope = scopes.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        if (!services.GetRequiredService<ITokenSubjectResolver>().TryResolve(actor, out var subject) ||
            subject.TenantId != tenant || subject.CanonicalUserId is null) return null;
        var ownership = await services.GetRequiredService<IOwnershipModeProvider>().GetAsync(tenant, cancellationToken);
        if (!services.GetRequiredService<IEffectiveCapabilitiesResolver>().Resolve(ownership,
            services.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value.ProviderCapabilities).CanManageUsers) return null;
        UserRecord? user = null; TenantMembershipRecord? membership = null;
        IReadOnlyList<IamRevisionGuard>? editorGuards = null;
        var targetGuards = await IdentitySnapshot.CaptureAsync(services, tenant, userId, [], async () =>
        {
            var current = await services.GetRequiredService<IRevisionAwareUserStore>().FindWithRevisionAsync(tenant, userId, cancellationToken);
            if (current is null || expectedUserRevision is not null && current.Revision != expectedUserRevision || current.Record.Status != UserStatus.Active) return false;
            user = current.Record;
            membership = await services.GetRequiredService<ITenantMembershipStore>().FindAsync(tenant, user.Id, cancellationToken);
            if (membership is { Status: not TenantMembershipStatus.Active }) return false;
            var roleIds = user.RoleIds.Concat(membership?.RoleIds ?? new HashSet<string>()).Distinct(StringComparer.Ordinal).ToArray();
            editorGuards = await IdentitySnapshot.CaptureAsync(services, tenant, subject.CanonicalUserId, roleIds, async () =>
            {
                if (services.GetService<ICanonicalUserSignInService>() is not { } authority ||
                    !await authority.CanSignInAsync(new(tenant, subject.CanonicalUserId), cancellationToken: cancellationToken)) return false;
                var permissions = services.GetRequiredService<ICurrentSubjectPermissionAuthorizer>();
                foreach (var permission in new[] { DefaultIdentityPermissionKeys.IdentityLinksManage, DefaultIdentityPermissionKeys.IdentityUsersRead })
                    if (!await Granted(permission)) return false;
                foreach (var id in roleIds)
                {
                    var role = await services.GetRequiredService<IRoleStore>().FindAsync(tenant, id, cancellationToken);
                    if (role is null || options.Value.AllowedRoleIds.Count != 0 && !options.Value.AllowedRoleIds.Contains(role.Id, StringComparer.Ordinal)) return false;
                    foreach (var permission in role.Permissions)
                        if (!await Granted(permission, role)) return false;
                }
                foreach (var permission in user.DirectPermissions.Concat(membership?.DirectPermissions ?? new HashSet<string>()).Distinct(StringComparer.Ordinal))
                    if (!await Granted(permission)) return false;
                return true;

                async ValueTask<bool> Granted(string permission, object? resource = null) =>
                    (await permissions.AuthorizeAsync(new(tenant, subject.CanonicalUserId, permission, resource), cancellationToken)).Status == CurrentSubjectPermissionStatus.Granted;
            }, cancellationToken);
            return editorGuards is not null;
        }, cancellationToken);
        if (user is null || targetGuards is null || editorGuards is null || IdentitySnapshot.Merge(targetGuards, editorGuards) is not { } guards) return null;
        var owner = targetGuards.Single(guard => guard.Kind == IamRevisionGuardKind.User);
        return new(user, membership, owner, guards);
    }
}
