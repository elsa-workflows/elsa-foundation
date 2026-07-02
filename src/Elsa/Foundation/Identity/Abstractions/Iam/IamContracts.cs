using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Ownership;

namespace Elsa.Foundation.Identity.Abstractions.Iam;

public interface IUserStore
{
    ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default);

    ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default);
}

public interface IRoleStore
{
    ValueTask<RoleRecord?> FindAsync(string tenantId, string roleId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default);
}

public interface IApplicationStore
{
    ValueTask<ApplicationRecord?> FindAsync(string tenantId, string applicationId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ApplicationRecord application, CancellationToken cancellationToken = default);
}

public interface ICredentialStore
{
    ValueTask<CredentialRecord?> FindAsync(string tenantId, string credentialId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(CredentialRecord credential, CancellationToken cancellationToken = default);
}

public interface IExternalIdentityStore
{
    ValueTask<ExternalIdentityRecord?> FindBySubjectAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default);
}

public interface IClaimMappingStore
{
    ValueTask<IReadOnlyList<ClaimMappingRule>> ListForProviderAsync(string tenantId, string provider, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ClaimMappingRule rule, CancellationToken cancellationToken = default);
}

public interface IProviderConfigurationStore
{
    ValueTask<ProviderConfigurationRecord?> FindGlobalAsync(string provider, CancellationToken cancellationToken = default);

    ValueTask<ProviderConfigurationRecord?> FindForTenantAsync(string tenantId, string provider, CancellationToken cancellationToken = default);

    async ValueTask<ProviderConfigurationRecord?> FindEffectiveAsync(string tenantId, string provider, bool allowGlobalFallback = false, CancellationToken cancellationToken = default)
    {
        var tenantConfiguration = await FindForTenantAsync(tenantId, provider, cancellationToken);
        if (tenantConfiguration is not null)
            return tenantConfiguration;

        return allowGlobalFallback ? await FindGlobalAsync(provider, cancellationToken) : null;
    }
}

public interface ITenantMembershipStore
{
    ValueTask<TenantMembershipRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default);
}

public interface IUserManager
{
    ValueTask<UserRecord> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    ValueTask<UserRecord> LinkExternalIdentityAsync(LinkExternalIdentityRequest request, CancellationToken cancellationToken = default);

    ValueTask DisableAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
}

public interface IRoleManager
{
    ValueTask<RoleRecord> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default);

    ValueTask AssignPermissionAsync(RolePermissionAssignment request, CancellationToken cancellationToken = default);
}

public interface IApplicationManager
{
    ValueTask<ApplicationRecord> RegisterAsync(RegisterApplicationRequest request, CancellationToken cancellationToken = default);
}

public interface ICredentialManager
{
    ValueTask<CredentialIssueResult> IssueAsync(IssueCredentialRequest request, CancellationToken cancellationToken = default);

    ValueTask RotateAsync(RotateCredentialRequest request, CancellationToken cancellationToken = default);

    ValueTask RevokeAsync(RevokeCredentialRequest request, CancellationToken cancellationToken = default);
}

public interface IProviderManager
{
    ValueTask<ProviderConfigurationRecord?> FindEffectiveAsync(string tenantId, string provider, bool allowGlobalFallback = false, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken = default);
}

public interface IClaimMappingManager
{
    ValueTask<IReadOnlyList<ClaimMappingRule>> ListEffectiveRulesAsync(string tenantId, string provider, CancellationToken cancellationToken = default);
}

public sealed record UserRecord(
    string Id,
    string TenantId,
    string UserName,
    string? Email,
    string? DisplayName,
    UserStatus Status,
    ResourceOwnership Ownership,
    IReadOnlySet<string> RoleIds,
    IReadOnlySet<string> DirectPermissions);

public sealed record RoleRecord(
    string Id,
    string TenantId,
    string Name,
    string? Description,
    IReadOnlySet<string> Permissions,
    bool System);

public sealed record ApplicationRecord(
    string Id,
    string TenantId,
    string ClientId,
    string DisplayName,
    ApplicationType Type,
    ResourceOwnership Ownership,
    IReadOnlySet<string> AllowedGrantTypes,
    IReadOnlySet<string> Scopes);

public sealed record CredentialRecord(
    string Id,
    string TenantId,
    CredentialSubjectType SubjectType,
    string SubjectId,
    CredentialKind Kind,
    string HashedSecret,
    string HashAlgorithm,
    CredentialStatus Status,
    DateTimeOffset? ExpiresAt);

public sealed record ExternalIdentityRecord(
    string TenantId,
    string Provider,
    string ProviderSubject,
    string UserId,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastSeenAt,
    ExternalIdentityLinkPolicy LinkPolicy);

public sealed record ProviderConfigurationRecord(
    string Provider,
    string? TenantId,
    string Kind,
    bool Enabled,
    bool IsDefault,
    ProviderCapabilities Capabilities,
    IReadOnlyDictionary<string, string> Settings);

public sealed record TenantMembershipRecord(
    string TenantId,
    string UserId,
    TenantMembershipStatus Status,
    IReadOnlySet<string> RoleIds,
    IReadOnlySet<string> DirectPermissions);

public sealed record CreateUserRequest(string TenantId, string UserName, string? Email, string? DisplayName, ResourceOwnership Ownership);

public sealed record LinkExternalIdentityRequest(
    string TenantId,
    string Provider,
    string ProviderSubject,
    string UserId,
    ExternalIdentityLinkPolicy LinkPolicy,
    bool DuplicateEmailWasExplicitlyApproved);

public sealed record CreateRoleRequest(string TenantId, string Name, string? Description, IReadOnlySet<string> Permissions);

public sealed record RolePermissionAssignment(string TenantId, string RoleId, string Permission);

public sealed record RegisterApplicationRequest(string TenantId, string ClientId, string DisplayName, ApplicationType Type, IReadOnlySet<string> Scopes);

public sealed record IssueCredentialRequest(string TenantId, CredentialSubjectType SubjectType, string SubjectId, CredentialKind Kind, IReadOnlySet<string> Scopes);

public sealed record CredentialIssueResult(CredentialRecord Credential, string PlainTextSecret);

public sealed record RotateCredentialRequest(string TenantId, string CredentialId, DateTimeOffset? GraceUntil);

public sealed record RevokeCredentialRequest(string TenantId, string CredentialId, string Reason);

public enum UserStatus
{
    Active,
    Disabled,
    Locked
}

public enum ResourceOwnership
{
    Foundation,
    External
}

public enum ApplicationType
{
    Public,
    Confidential
}

public enum CredentialSubjectType
{
    User,
    Application
}

public enum CredentialKind
{
    ApiKey,
    ClientSecret
}

public enum CredentialStatus
{
    Active,
    Rotating,
    Revoked
}

public enum ExternalIdentityLinkPolicy
{
    Auto,
    Admin,
    Invite
}

public enum TenantMembershipStatus
{
    Active,
    Suspended
}
