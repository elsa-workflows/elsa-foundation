namespace Elsa.Foundation.Identity.Core.Iam;

/// <summary>A policy-assessed user snapshot and qualified login committed at one authority boundary.</summary>
public sealed record ExternalUserAdmission(
    ExternalIdentityKey Key,
    UserRecord User,
    bool CreateUser,
    ExternalIdentityLinkPolicy LinkPolicy)
{
    /// <summary>Expected tenant membership when linking an existing user; null expects no membership.</summary>
    public TenantMembershipRecord? ExpectedMembership { get; init; }
}

/// <summary>Successful admission returns the canonical owner, including the winner of a concurrent first sign-in.</summary>
public sealed record ExternalUserAdmissionResult(bool Admitted, string? UserId = null, string? Failure = null);
