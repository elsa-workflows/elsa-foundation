using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>
/// Re-derives a subject's current grants from the persisted Elsa IAM records through the same
/// <see cref="IdentityClaimsProjector"/> used at sign-in, then applies the canonical permission decision.
/// A missing, disabled or locked user is denied; nothing from an earlier sign-in is reused.
/// </summary>
internal sealed class AspNetCoreIdentityCurrentSubjectPermissionAuthorizer(
    IUserStore users,
    IRoleStore roles,
    ITenantMembershipStore memberships,
    IPermissionAuthorizationService authorization) : ICurrentSubjectPermissionAuthorizer
{
    // The same normalized type AspNetCoreIdentityPrincipalFactory issues; registered as trusted by this provider.
    private const string AuthenticationType = "Elsa.Foundation.Identity";

    public async ValueTask<CurrentSubjectPermissionResult> AuthorizeAsync(
        CurrentSubjectPermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Permission);

        var user = await users.FindAsync(request.TenantId, request.SubjectId, cancellationToken);
        if (user is null || !string.Equals(user.Id, request.SubjectId, StringComparison.Ordinal) ||
            !string.Equals(user.TenantId, request.TenantId, StringComparison.Ordinal))
            return CurrentSubjectPermissionResult.Denied("The subject is not a current user of this tenant.");
        if (user.Status != UserStatus.Active)
            return CurrentSubjectPermissionResult.Denied("The subject is not active.");

        var identity = new ClaimsIdentity(
            authenticationType: AuthenticationType,
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);
        await IdentityClaimsProjector.ApplyUserClaimsAsync(identity, user, roles, memberships, cancellationToken);
        var result = await authorization.AuthorizeAsync(
            new PermissionEvaluationContext(new ClaimsPrincipal(identity), request.Permission, request.TenantId, request.Resource),
            cancellationToken);
        return result.Succeeded
            ? CurrentSubjectPermissionResult.Granted
            : CurrentSubjectPermissionResult.Denied(result.Failure ?? "The subject does not currently hold the permission.");
    }
}
