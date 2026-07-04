using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

public sealed class AspNetCoreIdentityPrincipalFactory(
    IUserStore users,
    IRoleStore roles,
    IExternalIdentityStore externalIdentities,
    ITenantMembershipStore memberships,
    IUserManager userManager,
    IClaimsNormalizer normalizer) : IPrincipalFactory
{
    public async ValueTask<ClaimsPrincipal> CreateAsync(PrincipalFactoryContext context, CancellationToken cancellationToken = default)
    {
        var externalIdentity = await externalIdentities.FindBySubjectAsync(context.TenantId, context.Provider, context.ProviderSubject, cancellationToken);
        var user = externalIdentity is not null
            ? await users.FindAsync(context.TenantId, externalIdentity.UserId, cancellationToken)
            : null;

        if (user is null)
        {
            user = await userManager.CreateAsync(new CreateUserRequest(
                context.TenantId,
                ResolveUserName(context.ExternalPrincipal, context.ProviderSubject),
                ResolveEmail(context.ExternalPrincipal),
                ResolveDisplayName(context.ExternalPrincipal),
                ResourceOwnership.External), cancellationToken);

            await userManager.LinkExternalIdentityAsync(new LinkExternalIdentityRequest(
                context.TenantId,
                context.Provider,
                context.ProviderSubject,
                user.Id,
                ExternalIdentityLinkPolicy.Auto,
                DuplicateEmailWasExplicitlyApproved: false), cancellationToken);
        }

        var normalized = await normalizer.NormalizeAsync(new ClaimsNormalizationContext(
            context.ExternalPrincipal,
            context.TenantId,
            context.Provider,
            context.MappingRules), cancellationToken);

        var identity = new ClaimsIdentity(normalized.Principal.Claims, "Elsa.Foundation.Identity");
        AddIfMissing(identity, ClaimTypes.NameIdentifier, user.Id);
        AddIfMissing(identity, "sub", user.Id);

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            AddIfMissing(identity, ClaimTypes.Name, user.DisplayName);

        if (!string.IsNullOrWhiteSpace(user.Email))
            AddIfMissing(identity, ClaimTypes.Email, user.Email);

        var roleIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in user.RoleIds)
        {
            roleIds.Add(role);
            AddIfMissing(identity, IdentityClaimTypes.Role, role);
        }

        foreach (var permission in user.DirectPermissions)
            AddIfMissing(identity, IdentityClaimTypes.Permission, permission);

        var membership = await memberships.FindAsync(context.TenantId, user.Id, cancellationToken);
        if (membership is not null && membership.Status == TenantMembershipStatus.Active)
        {
            foreach (var role in membership.RoleIds)
            {
                roleIds.Add(role);
                AddIfMissing(identity, IdentityClaimTypes.Role, role);
            }

            foreach (var permission in membership.DirectPermissions)
                AddIfMissing(identity, IdentityClaimTypes.Permission, permission);
        }

        // Expand role-granted permissions so that permissions assigned to a role (RoleRecord.Permissions)
        // are honoured by permission-based authorization, not just permissions assigned directly to the user.
        foreach (var roleId in roleIds)
        {
            var role = await roles.FindAsync(context.TenantId, roleId, cancellationToken);
            if (role is null)
                continue;

            foreach (var permission in role.Permissions)
                AddIfMissing(identity, IdentityClaimTypes.Permission, permission);
        }

        return new ClaimsPrincipal(identity);
    }

    private static string ResolveUserName(ClaimsPrincipal principal, string providerSubject) =>
        principal.FindFirst(ClaimTypes.Name)?.Value ??
        principal.FindFirst("preferred_username")?.Value ??
        principal.FindFirst(ClaimTypes.Email)?.Value ??
        providerSubject;

    private static string? ResolveEmail(ClaimsPrincipal principal) => principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst("email")?.Value;

    private static string? ResolveDisplayName(ClaimsPrincipal principal) => principal.FindFirst(ClaimTypes.Name)?.Value ?? principal.Identity?.Name;

    private static void AddIfMissing(ClaimsIdentity identity, string type, string value)
    {
        if (!identity.HasClaim(type, value))
            identity.AddClaim(new Claim(type, value));
    }
}
