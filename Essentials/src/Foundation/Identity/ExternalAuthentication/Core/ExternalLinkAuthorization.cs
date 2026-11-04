using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Core;

/// <summary>Server-owned unchanged target and the live IAM roots checked for link administration.</summary>
public sealed class ExternalLinkAuthorization(UserRecord user, TenantMembershipRecord? membership,
    IamRevisionGuard owner, IReadOnlyList<IamRevisionGuard> guards)
{
    public UserRecord User { get; } = user;
    public TenantMembershipRecord? Membership { get; } = membership;
    public IamRevisionGuard Owner { get; } = owner;
    public IReadOnlyList<IamRevisionGuard> Guards { get; } = guards;
}

[ReplacementContract]
public interface IExternalLinkAuthorizer
{
    ValueTask<ExternalLinkAuthorization?> CaptureAsync(ClaimsPrincipal actor, string userId, string? expectedUserRevision = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Captures a proved remaining user path and administrative recovery, excluding the link being removed.</summary>
[ReplacementContract]
public interface IExternalLinkRecovery
{
    ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureAsync(ClaimsPrincipal actor, ExternalLinkAuthorization authorization,
        ExternalIdentityKey excluded, CancellationToken cancellationToken = default);
}
