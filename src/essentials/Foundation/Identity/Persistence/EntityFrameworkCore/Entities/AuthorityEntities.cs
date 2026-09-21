using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational authority root for one tenant-local Identity user.</summary>
public sealed class UserEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string UserId { get; set; } = "";
    public byte[] UserIdOrderKey { get; set; } = [];
    public string UserName { get; set; } = "";
    public string? NormalizedUserName { get; set; }
    public string? NormalizedUserNameKey { get; set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public string? NormalizedEmailKey { get; set; }
    public string? DisplayName { get; set; }
    public int Status { get; set; }
    public int Ownership { get; set; }
    public string RoleIdsJson { get; set; } = "[]";
    public string DirectPermissionsJson { get; set; } = "[]";
    public string ClaimIdsJson { get; set; } = "[]";
    public string LoginIdsJson { get; set; } = "[]";
    public string RoleLinkIdsJson { get; set; } = "[]";
    public string TokenIdsJson { get; set; } = "[]";
    public string TenantMembershipIdsJson { get; set; } = "[]";
    public bool EmailConfirmed { get; set; }
    public string? PasswordHash { get; set; }
    public string? SecurityStamp { get; set; }
    public string? ConcurrencyStamp { get; set; }
    public string? PhoneNumber { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool LockoutEnabled { get; set; }
    public int AccessFailedCount { get; set; }
    public long Revision { get; set; }
}

/// <summary>Relational authority root for one tenant-local Identity role.</summary>
public sealed class RoleEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string RoleId { get; set; } = "";
    public byte[] RoleIdOrderKey { get; set; } = [];
    public string Name { get; set; } = "";
    public string? NormalizedName { get; set; }
    public string? NormalizedNameKey { get; set; }
    public string? Description { get; set; }
    public string PermissionsJson { get; set; } = "[]";
    public bool System { get; set; }
    public string ClaimIdsJson { get; set; } = "[]";
    public string UserLinkIdsJson { get; set; } = "[]";
    public string? ConcurrencyStamp { get; set; }
    public long Revision { get; set; }
}

public sealed class ClaimMappingEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderLookupKey { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string RuleLookupKey { get; set; } = "";
    public byte[] RuleIdOrderKey { get; set; } = [];
    public string MatchClaimType { get; set; } = "";
    public string MatchValue { get; set; } = "";
    public string GrantRolesJson { get; set; } = "[]";
    public string GrantPermissionsJson { get; set; } = "[]";
    public int Order { get; set; }
    public bool StopOnMatch { get; set; }
    public long Revision { get; set; }
}

public sealed class ExternalIdentityEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string Provider { get; set; } = "";
    public string? ProviderDisplayName { get; set; }
    public string ProviderLookupKey { get; set; } = "";
    public string ProviderSubject { get; set; } = "";
    public string ProviderSubjectLookupKey { get; set; } = "";
    public byte[] ExternalOrderKey { get; set; } = [];
    public string UserId { get; set; } = "";
    public string UserLookupKey { get; set; } = "";
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public int LinkPolicy { get; set; }
    public long Revision { get; set; }
}

public sealed class UserClaimEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserLookupKey { get; set; } = "";
    public string ClaimType { get; set; } = "";
    public string? ClaimValue { get; set; }
    public string ClaimKey { get; set; } = "";
    public long Revision { get; set; }
}

public sealed class RoleClaimEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string RoleLookupKey { get; set; } = "";
    public string ClaimType { get; set; } = "";
    public string? ClaimValue { get; set; }
    public string ClaimKey { get; set; } = "";
    public long Revision { get; set; }
}

public sealed class UserRoleEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserLookupKey { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string RoleLookupKey { get; set; } = "";
    public long Revision { get; set; }
}

public sealed class UserTokenEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserLookupKey { get; set; } = "";
    public string LoginProvider { get; set; } = "";
    public string Name { get; set; } = "";
    public string TokenKey { get; set; } = "";
    public string? Value { get; set; }
    public long Revision { get; set; }
}

public sealed class TenantMembershipEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserLookupKey { get; set; } = "";
    public int Status { get; set; }
    public string RoleIdsJson { get; set; } = "[]";
    public string DirectPermissionsJson { get; set; } = "[]";
    public long Revision { get; set; }
}

public sealed class UserNameReservationEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string NormalizedUserName { get; set; } = "";
    public string NormalizedUserNameKey { get; set; } = "";
    public string UserId { get; set; } = "";
    public long Revision { get; set; }
}

public sealed class EmailReservationEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string NormalizedEmailKey { get; set; } = "";
    public string UserId { get; set; } = "";
    public long Revision { get; set; }
}

public sealed class RoleNameReservationEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string NormalizedRoleName { get; set; } = "";
    public string NormalizedRoleNameKey { get; set; } = "";
    public string RoleId { get; set; } = "";
    public long Revision { get; set; }
}

public sealed class MutationReceiptEntity
{
    public string Id { get; set; } = "";
    public string MutationReceiptId { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string RequestFingerprint { get; set; } = "";
    public int Status { get; set; }
    public long? Version { get; set; }
    public string Message { get; set; } = "";
    public string? AuthoritativeId { get; set; }
    public string? FailedUnitId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long Revision { get; set; }
}
