using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

/// <summary>
/// Deterministic identity records used by both the store behavior tests and the golden-fixture tests.
/// Fixed ids and <see cref="DateTimeOffset.UnixEpoch"/> timestamps keep the serialized shape stable so the
/// golden fixtures never drift on incidental values.
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

    public static InMemoryDocumentStore NewDocumentStore() => new(IdentityStorageManifest.Create());

    public static GroundworkUserStore UserStore(IDocumentStore store, string scope = "tenant-1") =>
        new(store, Accessor(scope));

    public static GroundworkRoleStore RoleStore(IDocumentStore store, string scope = "tenant-1") =>
        new(store, Accessor(scope));

    public static GroundworkApplicationStore ApplicationStore(IDocumentStore store, string scope = "tenant-1") =>
        new(store, Accessor(scope));

    public static GroundworkCredentialStore CredentialStore(IDocumentStore store, string scope = "tenant-1") =>
        new(store, Accessor(scope));

    public static GroundworkClaimMappingStore ClaimMappingStore(IDocumentStore store, string scope = "tenant-1") =>
        new(store, Accessor(scope));

    public static GroundworkProviderConfigurationStore ProviderConfigurationStore(IDocumentStore store, string scope = "tenant-1") =>
        new(store, Accessor(scope));

    public static GroundworkProviderConfigurationStore GlobalProviderConfigurationStore(IDocumentStore store) =>
        new(store, new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedGlobal(
            new PersistenceAccessPurpose("seed-global-provider-configuration"))));

    public static GroundworkExternalIdentityStore ExternalIdentityStore(IDocumentStore store, string scope = "tenant-1") =>
        new(store, Accessor(scope));

    public static GroundworkTenantMembershipStore TenantMembershipStore(IDocumentStore store, string scope = "tenant-1") =>
        new(store, Accessor(scope));

    public static IPersistenceAccessContextAccessor Accessor(string scope = "tenant-1") =>
        new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
