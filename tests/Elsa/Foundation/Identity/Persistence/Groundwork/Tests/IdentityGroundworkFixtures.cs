using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Store;
using Groundwork.Testing;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

/// <summary>
/// Deterministic identity records used by the public-v2 store behavior tests.
/// Fixed ids and <see cref="DateTimeOffset.UnixEpoch"/> timestamps keep assertions stable.
/// </summary>
internal static class IdentityGroundworkFixtures
{
    public static UserRecord User() => new(
        Id: "user-1",
        TenantId: "tenant-1",
        UserName: "alice",
        Email: "alice@example.com",
        DisplayName: "Alice Example",
        Status: UserStatus.Active,
        Ownership: ResourceOwnership.Foundation,
        RoleIds: new HashSet<string> { "role-1" },
        DirectPermissions: new HashSet<string> { "secrets:read" });

    public static RoleRecord Role() => new(
        Id: "role-1",
        TenantId: "tenant-1",
        Name: "Administrators",
        Description: "Full access",
        Permissions: new HashSet<string> { "secrets:read", "secrets:write" },
        System: false);

    public static ApplicationRecord Application() => new(
        Id: "app-1",
        TenantId: "tenant-1",
        ClientId: "client-1",
        DisplayName: "Workflow Client",
        Type: ApplicationType.Confidential,
        Ownership: ResourceOwnership.Foundation,
        AllowedGrantTypes: new HashSet<string> { "client_credentials" },
        Scopes: new HashSet<string> { "workflows:run" });

    public static CredentialRecord Credential() => new(
        Id: "credential-1",
        TenantId: "tenant-1",
        SubjectType: CredentialSubjectType.Application,
        SubjectId: "app-1",
        Kind: CredentialKind.ClientSecret,
        HashedSecret: "sha256:credential-hash",
        HashAlgorithm: "SHA-256",
        Status: CredentialStatus.Active,
        ExpiresAt: DateTimeOffset.UnixEpoch.AddDays(90));

    public static ClaimMappingRule ClaimMappingRule() => new(
        Id: "claim-map-1",
        TenantId: "tenant-1",
        Provider: "google",
        MatchClaimType: "groups",
        MatchValue: "admins",
        GrantRoles: new HashSet<string> { "role-1" },
        GrantPermissions: new HashSet<string> { "secrets:read" },
        Order: 10,
        StopOnMatch: true);

    public static ProviderConfigurationRecord TenantProviderConfiguration() => new(
        Provider: "google",
        TenantId: "tenant-1",
        Kind: "external-oidc",
        Enabled: true,
        IsDefault: true,
        Capabilities: ProviderCapabilities.ExternalOidcDefault,
        Settings: new Dictionary<string, string> { ["authority"] = "https://issuer.example" });

    public static ProviderConfigurationRecord GlobalProviderConfiguration() => new(
        Provider: "google",
        TenantId: null,
        Kind: "external-oidc",
        Enabled: true,
        IsDefault: false,
        Capabilities: ProviderCapabilities.ExternalOidcDefault,
        Settings: new Dictionary<string, string> { ["authority"] = "https://global-issuer.example" });

    public static ExternalIdentityRecord ExternalIdentity() => new(
        TenantId: "tenant-1",
        Provider: "google",
        ProviderSubject: "sub-123",
        UserId: "user-1",
        LinkedAt: DateTimeOffset.UnixEpoch,
        LastSeenAt: DateTimeOffset.UnixEpoch,
        LinkPolicy: ExternalIdentityLinkPolicy.Auto);

    public static TenantMembershipRecord TenantMembership() => new(
        TenantId: "tenant-1",
        UserId: "user-1",
        Status: TenantMembershipStatus.Active,
        RoleIds: new HashSet<string> { "role-1" },
        DirectPermissions: new HashSet<string> { "secrets:read" });

    public static IdentityTestPersistence NewDocumentStore() => new();

    public static GroundworkUserStore UserStore(IdentityTestPersistence persistence, string scope = "tenant-1") =>
        new(persistence.Rows(Accessor(scope)), Accessor(scope));

    public static GroundworkRoleStore RoleStore(IdentityTestPersistence persistence, string scope = "tenant-1") =>
        new(persistence.Rows(Accessor(scope)), Accessor(scope));

    public static GroundworkApplicationStore ApplicationStore(IdentityTestPersistence persistence, string scope = "tenant-1") =>
        new(persistence.Rows(Accessor(scope)), Accessor(scope));

    public static GroundworkCredentialStore CredentialStore(IdentityTestPersistence persistence, string scope = "tenant-1") =>
        new(persistence.Rows(Accessor(scope)), Accessor(scope));

    public static GroundworkClaimMappingStore ClaimMappingStore(IdentityTestPersistence persistence, string scope = "tenant-1") =>
        new(persistence.Rows(Accessor(scope)), Accessor(scope));

    public static GroundworkProviderConfigurationStore ProviderConfigurationStore(IdentityTestPersistence persistence, string scope = "tenant-1") =>
        new(persistence.Rows(Accessor(scope)), Accessor(scope));

    public static GroundworkProviderConfigurationStore GlobalProviderConfigurationStore(IdentityTestPersistence persistence)
    {
        var access = new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedGlobal(
            new PersistenceAccessPurpose("seed-global-provider-configuration")));
        return new GroundworkProviderConfigurationStore(persistence.Rows(access), access);
    }

    public static GroundworkExternalIdentityStore ExternalIdentityStore(IdentityTestPersistence persistence, string scope = "tenant-1") =>
        new(persistence.Rows(Accessor(scope)), Accessor(scope));

    public static GroundworkTenantMembershipStore TenantMembershipStore(IdentityTestPersistence persistence, string scope = "tenant-1") =>
        new(persistence.Rows(Accessor(scope)), Accessor(scope));

    public static IPersistenceAccessContextAccessor Accessor(string scope = "tenant-1") =>
        new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    public static IPersistenceAccessContextAccessor GlobalAccessor() =>
        new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedGlobal(
            new PersistenceAccessPurpose("identity-test-global")));

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}

internal sealed class IdentityTestPersistence : IGroundworkStorageSessionSource, IDisposable
{
    private readonly IReadOnlyDictionary<string, StorageUnit> units = IdentityV2StorageManifest.CreateUnits()
        .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
    private readonly IStorageProviderConnection connection;

    public IdentityTestPersistence()
    {
        connection = new InMemoryProviderFactory().Create($"identity-tests:{Guid.NewGuid():N}");
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);
    }

    public GroundworkIdentityRowStore Rows(IPersistenceAccessContextAccessor access) => new(this, access);

    public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
        connection.OpenSession(Unit(unitId, targetName), access);

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IReadOnlyList<string> unitIds,
        string? targetName = null) =>
        connection.BeginUnitOfWork(access, options, unitIds.Select(id => Unit(id, targetName)).ToArray());

    public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

    public void Dispose() => connection.Dispose();
}
