using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Core;

/// <summary>Server-only input from a validating protocol adapter; never deserialize this from a client request.</summary>
public sealed record ExternalAdmissionContext(ConnectionView Connection, ExternalIdentityKey Identity, IReadOnlyList<Claim> Claims);

public enum ExternalAdmissionKind { Rejected, ExistingUser, NewUser }
public enum ExternalAdmissionFailure
{
    None, UnknownIdentity, IdentityMismatch, IdentityUnavailable, UserUnavailable, MembershipUnavailable,
    PolicyUnavailable, PolicyRejected, VerifiedEmailRequired, NoEligibleMatch, OwnershipDenied,
    InvalidProfile, RoleUnavailable, RoleBoundary, ConnectionUnavailable, StaleConnection, Conflict
}

/// <summary>Pure policy output. New-user role IDs must be selected from the connection's authorized defaults.</summary>
public sealed record ExternalProvisioningDecision
{
    public ExternalAdmissionKind Kind { get; init; } = ExternalAdmissionKind.Rejected;
    public ExternalAdmissionFailure Failure { get; init; } = ExternalAdmissionFailure.PolicyRejected;
    public string? ExistingUserId { get; init; }
    public string? UserName { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public IReadOnlyList<string> RoleIds { get; init; } = [];
}

/// <summary>
/// Installed, explicitly selected integrator policy. Assessment must perform no writes, provisioning,
/// session creation or external side effects: the same call supplies interactive preview.
/// </summary>
public interface IExternalProvisioningPolicy
{
    string Name { get; }
    ValueTask<ExternalProvisioningDecision> AssessAsync(ExternalAdmissionContext context, CancellationToken cancellationToken = default);
}

/// <summary>A prospective authorization snapshot; a new user's ID is empty until actual admission.</summary>
public sealed record ExternalAdmissionAssessment(
    ExternalAdmissionKind Kind,
    UserRecord? User,
    IReadOnlySet<string> RoleIds,
    IReadOnlySet<string> Permissions,
    ExternalAdmissionFailure Failure = ExternalAdmissionFailure.None)
{
    /// <summary>Server-side snapshot carried only to the atomic existing-user link operation.</summary>
    public TenantMembershipRecord? Membership { get; init; }
}

public sealed record ExternalAdmissionResult(string? UserId, ExternalAdmissionFailure Failure = ExternalAdmissionFailure.None);

[ReplacementContract]
public interface IExternalAdmissionService
{
    ValueTask<ExternalAdmissionAssessment> AssessAsync(ExternalAdmissionContext context, CancellationToken cancellationToken = default);
    /// <summary>Reassesses current state, then invokes the authority's atomic mutation. Never used for preview.</summary>
    ValueTask<ExternalAdmissionResult> AdmitAsync(ExternalAdmissionContext context, CancellationToken cancellationToken = default);
}
