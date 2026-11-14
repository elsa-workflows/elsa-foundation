using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Ownership;

namespace Elsa.Foundation.Identity.Core.Iam;

[ReplacementContract]
public interface IUserStore
{
    ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Returns only a unique normalized email match; missing or ambiguous addresses return null.</summary>
    ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the user's <see cref="UserRecord.Status"/> and nothing else; returns <c>false</c> only when the user
    /// does not exist. Setting the status the user already has is a no-op that leaves the record and its revision
    /// unchanged. If another write to the same user commits while the status is being set, the store never
    /// overwrites it: it either applies the status on top of that write or throws without changing anything, and
    /// the caller may retry. That case is never reported as <c>false</c>.
    /// </summary>
    ValueTask<bool> SetStatusAsync(string tenantId, string userId, UserStatus status, CancellationToken cancellationToken = default);

    /// <summary>Deletes the user together with the records it owns; returns <c>false</c> when the user does not exist.</summary>
    ValueTask<bool> DeleteAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
}

/// <summary>Provider-neutral bounded paging contract for tenant-local user enumeration and search.</summary>
[ReplacementContract]
public interface IPagedUserStore
{
    /// <summary>
    /// Pages the tenant's users in a stable order. A non-blank <paramref name="search"/> keeps only users whose
    /// user name or email equals it once both are normalized the way the store normalizes that field when it
    /// saves a user, so a user is found by the value it was saved with, whichever write path saved it.
    /// </summary>
    ValueTask<IamPage<UserRecord>> ListPageAsync(
        string tenantId,
        IamPageRequest request,
        string? search = null,
        CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IRevisionAwareUserStore
{
    /// <summary>Captures a non-reusable account-instance/revision binding for a coordinated write; null means unsupported or absent.</summary>
    ValueTask<IamRevisionGuard?> FindRevisionGuardAsync(string tenantId, string userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IamRevisionGuard?>(null);

    ValueTask<IamRevisionedRecord<UserRecord>?> FindWithRevisionAsync(string tenantId, string userId, CancellationToken cancellationToken = default);

    ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(UserRecord user, string? expectedRevision, CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IRoleStore
{
    ValueTask<RoleRecord?> FindAsync(string tenantId, string roleId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IRevisionAwareRoleStore
{
    /// <summary>Captures the role definition and revision for a coordinated write; null means unsupported or absent.</summary>
    ValueTask<IamRevisionGuard?> FindRevisionGuardAsync(string tenantId, string roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IamRevisionGuard?>(null);

    ValueTask<IamRevisionedRecord<RoleRecord>?> FindWithRevisionAsync(string tenantId, string roleId, CancellationToken cancellationToken = default);

    ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(RoleRecord role, string? expectedRevision, CancellationToken cancellationToken = default);
}

/// <summary>Provider-neutral bounded paging contract for tenant-local role enumeration.</summary>
[ReplacementContract]
public interface IPagedRoleStore
{
    ValueTask<IamPage<RoleRecord>> ListPageAsync(
        string tenantId,
        IamPageRequest request,
        CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IApplicationStore
{
    ValueTask<ApplicationRecord?> FindAsync(string tenantId, string applicationId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ApplicationRecord application, CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IRevisionAwareApplicationStore
{
    ValueTask<IamRevisionedRecord<ApplicationRecord>?> FindWithRevisionAsync(string tenantId, string applicationId, CancellationToken cancellationToken = default);

    ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(ApplicationRecord application, string? expectedRevision, CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface ICredentialStore
{
    ValueTask<CredentialRecord?> FindAsync(string tenantId, string credentialId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(CredentialRecord credential, CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IRevisionAwareCredentialStore
{
    ValueTask<IamRevisionedRecord<CredentialRecord>?> FindWithRevisionAsync(string tenantId, string credentialId, CancellationToken cancellationToken = default);

    ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(CredentialRecord credential, string? expectedRevision, CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IExternalIdentityStore
{
    bool SupportsQualifiedKeys => false;
    bool SupportsAtomicAdmission => false;

    /// <summary>Commits a policy-assessed new user and login atomically, or links an unchanged active existing user.</summary>
    ValueTask<ExternalUserAdmissionResult> AdmitAsync(ExternalUserAdmission request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This external identity authority does not support atomic admission.");

    /// <summary>Finds an exact issuer/subject link. Legacy issuer-less links are never used as a fallback.</summary>
    ValueTask<ExternalIdentityRecord?> FindByKeyAsync(ExternalIdentityKey key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This external identity authority does not support issuer-qualified links.");

    ValueTask<ExternalIdentityRecord?> FindBySubjectAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IRevisionAwareExternalIdentityStore
{
    bool SupportsQualifiedKeys => false;
    bool SupportsQualifiedRemoval => false;
    /// <summary>Owns a conditional qualified removal and advances the owner counter beyond the removed link without changing other credentials.</summary>
    ValueTask<ExternalIdentityRemovalResult> RemoveWithRevisionAsync(ExternalIdentityKey key, IamRevisionGuard owner,
        string expectedLinkRevision, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This identity authority does not support conditional qualified removal.");

    ValueTask<IamRevisionedRecord<ExternalIdentityRecord>?> FindByKeyWithRevisionAsync(ExternalIdentityKey key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This external identity authority does not support issuer-qualified links.");

    ValueTask<IamRevisionedRecord<ExternalIdentityRecord>?> FindBySubjectWithRevisionAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default);

    ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(ExternalIdentityRecord externalIdentity, string? expectedRevision, CancellationToken cancellationToken = default);
}

/// <summary>Provider-neutral bounded paging contract for external identities belonging to one tenant user.</summary>
[ReplacementContract]
public interface IPagedExternalIdentityStore
{
    ValueTask<IamPage<ExternalIdentityRecord>> ListForUserPageAsync(
        string tenantId,
        string userId,
        IamPageRequest request,
        CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IClaimMappingStore
{
    ValueTask<IReadOnlyList<ClaimMappingRule>> ListForProviderAsync(string tenantId, string provider, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ClaimMappingRule rule, CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IRevisionAwareClaimMappingStore
{
    ValueTask<IamRevisionedRecord<ClaimMappingRule>?> FindWithRevisionAsync(string tenantId, string provider, string ruleId, CancellationToken cancellationToken = default);

    ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(ClaimMappingRule rule, string? expectedRevision, CancellationToken cancellationToken = default);
}

/// <summary>Provider-neutral bounded paging contract for one tenant/provider claim-mapping collection.</summary>
[ReplacementContract]
public interface IPagedClaimMappingStore
{
    ValueTask<IamPage<ClaimMappingRule>> ListForProviderPageAsync(
        string tenantId,
        string provider,
        IamPageRequest request,
        CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IProviderConfigurationStore
{
    ValueTask<ProviderConfigurationRecord?> FindGlobalAsync(string provider, CancellationToken cancellationToken = default);

    ValueTask<ProviderConfigurationRecord?> FindForTenantAsync(string tenantId, string provider, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken = default);

    async ValueTask<ProviderConfigurationRecord?> FindEffectiveAsync(string tenantId, string provider, bool allowGlobalFallback = false, CancellationToken cancellationToken = default)
    {
        var tenantConfiguration = await FindForTenantAsync(tenantId, provider, cancellationToken);
        if (tenantConfiguration is not null)
            return tenantConfiguration;

        return allowGlobalFallback ? await FindGlobalAsync(provider, cancellationToken) : null;
    }
}

[ReplacementContract]
public interface IRevisionAwareProviderConfigurationStore
{
    /// <summary>True only when IAM roots and provider configuration can commit in one physical transaction.</summary>
    bool SupportsIdentityRevisionGuards => false;

    /// <summary>CAS-saves a global configuration and advances the guarded IAM revisions as one committed unit.</summary>
    ValueTask<IamRevisionSaveResult> SaveWithIdentityRevisionGuardsAsync(ProviderConfigurationRecord configuration, string? expectedRevision,
        IReadOnlyList<IamRevisionGuard> guards, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This provider configuration authority cannot coordinate IAM revisions.");

    ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindGlobalWithRevisionAsync(string provider, CancellationToken cancellationToken = default);

    ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindForTenantWithRevisionAsync(string tenantId, string provider, CancellationToken cancellationToken = default);

    ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(ProviderConfigurationRecord configuration, string? expectedRevision, CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface ITenantMembershipStore
{
    ValueTask<TenantMembershipRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IRevisionAwareTenantMembershipStore
{
    ValueTask<IamRevisionedRecord<TenantMembershipRecord>?> FindWithRevisionAsync(string tenantId, string userId, CancellationToken cancellationToken = default);

    ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(TenantMembershipRecord membership, string? expectedRevision, CancellationToken cancellationToken = default);
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

public sealed record IamRevisionedRecord<TRecord>(TRecord Record, string Revision);

public sealed record IamRevisionSaveResult(IamRevisionSaveStatus Status, string? Revision = null);

public enum IamRevisionSaveStatus
{
    Saved,
    Conflict,
    NotFound
}

/// <summary>Finite, deterministic page request shared by IAM contracts without a persistence-provider dependency.</summary>
public sealed record IamPageRequest
{
    public const int MaximumTake = 250;

    public IamPageRequest(int skip = 0, int take = 50)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        if (take is <= 0 or > MaximumTake)
            throw new ArgumentOutOfRangeException(nameof(take), take, $"IAM page size must be between 1 and {MaximumTake}.");

        Skip = skip;
        Take = take;
    }

    public int Skip { get; }
    public int Take { get; }
}

/// <summary>Bounded result with the provider-observed total count for the same query evaluation.</summary>
public sealed record IamPage<TRecord>
{
    public IamPage(IReadOnlyList<TRecord> items, long totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        if (totalCount < items.Count)
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count cannot be smaller than the returned IAM page.");

        Items = items;
        TotalCount = totalCount;
    }

    public IReadOnlyList<TRecord> Items { get; }
    public long TotalCount { get; }
}

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
    ExternalIdentityLinkPolicy LinkPolicy)
{
    /// <summary>Validated issuer, compared ordinally. Null identifies the separate legacy ASP.NET login namespace.</summary>
    public string? Issuer { get; init; }
}

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
    bool DuplicateEmailWasExplicitlyApproved)
{
    public string? Issuer { get; init; }
}

public sealed record CreateRoleRequest(string TenantId, string Name, string? Description, IReadOnlySet<string> Permissions);

public sealed record RolePermissionAssignment(string TenantId, string RoleId, string Permission);

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
