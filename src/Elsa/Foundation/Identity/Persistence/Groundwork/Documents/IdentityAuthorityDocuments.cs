using Elsa.Foundation.Identity.Abstractions.Iam;

using System.Text.Json.Serialization;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Documents;

public sealed record IdentityUserDocument(
    string TenantId,
    string UserId,
    string? NormalizedUserName,
    string? NormalizedEmail,
    string? NormalizedUserNameKey,
    string? NormalizedEmailKey,
    UserRecord User,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IdentityUserFrameworkState? FrameworkState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyCollection<string>? ClaimIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyCollection<string>? LoginIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyCollection<string>? RoleLinkIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyCollection<string>? TokenIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyCollection<string>? TenantMembershipIds = null);

public sealed record IdentityRoleDocument(
    string TenantId,
    string RoleId,
    string? NormalizedRoleName,
    string? NormalizedRoleNameKey,
    RoleRecord Role,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IdentityRoleFrameworkState? FrameworkState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyCollection<string>? ClaimIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyCollection<string>? UserLinkIds = null);

public sealed record IdentityUserClaimDocument(
    string TenantId,
    string UserId,
    string ClaimType,
    string? ClaimValue,
    string ClaimKey,
    string? UserLookupKey = null);

public sealed record IdentityRoleClaimDocument(
    string TenantId,
    string RoleId,
    string ClaimType,
    string? ClaimValue,
    string? RoleLookupKey = null);

public sealed record IdentityExternalLoginDocument(
    string TenantId,
    string UserId,
    string LoginProvider,
    string ProviderKey,
    string LoginKey,
    string? ProviderDisplayName,
    ExternalIdentityRecord ExternalIdentity,
    string? UserLookupKey = null);

public sealed record IdentityUserRoleDocument(
    string TenantId,
    string UserId,
    string RoleId,
    string? UserLookupKey = null,
    string? RoleLookupKey = null);

public sealed record IdentityUserTokenDocument(
    string TenantId,
    string UserId,
    string LoginProvider,
    string Name,
    string TokenKey,
    string? Value,
    string? UserLookupKey = null);

public sealed record IdentityTenantMembershipDocument(string TenantId, string UserId, string MembershipKey, string Status, TenantMembershipRecord TenantMembership);

public sealed record IdentityUserNameReservationDocument(string TenantId, string NormalizedUserName, string UserNameReservationKey, string UserId);

public sealed record IdentityEmailReservationDocument(string TenantId, string NormalizedEmail, string EmailReservationKey, string UserId);

public sealed record IdentityRoleNameReservationDocument(string TenantId, string NormalizedRoleName, string RoleNameReservationKey, string RoleId);

public sealed record IdentityUserFrameworkState(
    string Id,
    string TenantId,
    string? UserName,
    string? NormalizedUserName,
    string? Email,
    string? NormalizedEmail,
    bool EmailConfirmed,
    string? PasswordHash,
    string? SecurityStamp,
    string? ConcurrencyStamp,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    bool TwoFactorEnabled,
    DateTimeOffset? LockoutEnd,
    bool LockoutEnabled,
    int AccessFailedCount,
    string? DisplayName);

public sealed record IdentityRoleFrameworkState(
    string Id,
    string? Name,
    string? NormalizedName,
    string? ConcurrencyStamp);
