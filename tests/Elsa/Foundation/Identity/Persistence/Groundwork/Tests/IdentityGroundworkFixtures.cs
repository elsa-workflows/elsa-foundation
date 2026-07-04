using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;

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
}
