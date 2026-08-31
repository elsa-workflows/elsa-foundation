using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Store;
using System.Text.Json;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

/// <summary>
/// Behavioral tests for the Groundwork-backed identity stores, including the restart-survival guarantee:
/// records written through one store instance are readable through a fresh store instance over the same
/// underlying document store, proving identity is durable rather than tied to a process-lifetime object.
/// </summary>
public sealed class IdentityGroundworkStoreTests
{
    [Fact]
    public async Task User_RoundTrips_By_Id_And_Email()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.UserStore(docStore);
        var user = IdentityGroundworkFixtures.User();

        await store.SaveAsync(user);

        var byId = await store.FindAsync(user.TenantId, user.Id);
        var byEmail = await store.FindByEmailAsync(user.TenantId, "ALICE@example.com");

        Assert.NotNull(byId);
        Assert.Equal("alice", byId!.UserName);
        Assert.NotNull(byEmail);
        Assert.Equal("user-1", byEmail!.Id);
    }

    [Fact]
    public async Task User_Survives_A_Store_Restart()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var user = IdentityGroundworkFixtures.User();

        await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(user);

        // Fresh store instance over the same durable document store == process restart.
        var reloaded = await IdentityGroundworkFixtures.UserStore(docStore).FindAsync(user.TenantId, user.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(user.Email, reloaded!.Email);
        Assert.Equal(user.RoleIds, reloaded.RoleIds);
    }

    [Fact]
    public async Task Role_Lists_By_Tenant_And_Survives_Restart()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var role = IdentityGroundworkFixtures.Role();

        await IdentityGroundworkFixtures.RoleStore(docStore).SaveAsync(role);

        var list = await IdentityGroundworkFixtures.RoleStore(docStore).ListAsync("tenant-1");

        Assert.Equal("role-1", Assert.Single(list).Id);
    }

    [Fact]
    public async Task Application_RoundTrips_And_Survives_Restart()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var application = IdentityGroundworkFixtures.Application();

        await IdentityGroundworkFixtures.ApplicationStore(docStore).SaveAsync(application);

        var reloaded = await IdentityGroundworkFixtures.ApplicationStore(docStore)
            .FindAsync(application.TenantId, application.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(application.ClientId, reloaded!.ClientId);
        Assert.Equal(application.AllowedGrantTypes, reloaded.AllowedGrantTypes);
        Assert.Equal(application.Scopes, reloaded.Scopes);
    }

    [Fact]
    public async Task Credential_RoundTrips_And_Survives_Restart()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var credential = IdentityGroundworkFixtures.Credential();

        await IdentityGroundworkFixtures.CredentialStore(docStore).SaveAsync(credential);

        var reloaded = await IdentityGroundworkFixtures.CredentialStore(docStore)
            .FindAsync(credential.TenantId, credential.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(credential.SubjectId, reloaded!.SubjectId);
        Assert.Equal(credential.HashedSecret, reloaded.HashedSecret);
        Assert.Equal(credential.ExpiresAt, reloaded.ExpiresAt);
    }

    [Fact]
    public async Task ClaimMapping_Lists_By_Provider_In_Deterministic_Order()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.ClaimMappingStore(docStore);
        var later = IdentityGroundworkFixtures.ClaimMappingRule() with { Id = "claim-map-2", Order = 20 };
        var earlier = IdentityGroundworkFixtures.ClaimMappingRule() with { Id = "claim-map-1", Order = 10 };
        var otherProvider = IdentityGroundworkFixtures.ClaimMappingRule() with { Id = "claim-map-3", Provider = "github" };

        await store.SaveAsync(later);
        await store.SaveAsync(otherProvider);
        await store.SaveAsync(earlier);

        var rules = await IdentityGroundworkFixtures.ClaimMappingStore(docStore)
            .ListForProviderAsync("tenant-1", "google");

        Assert.Equal(["claim-map-1", "claim-map-2"], rules.Select(rule => rule.Id));
    }

    [Fact]
    public async Task Legacy_role_list_refuses_overflow_and_names_the_paged_contract()
    {
        var persistence = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.RoleStore(persistence);
        for (var index = 0; index <= IdentityStorageManifest.MaxAggregateRelationshipEntries; index++)
        {
            var suffix = index.ToString("D3");
            await store.SaveAsync(IdentityGroundworkFixtures.Role() with
            {
                Id = $"role-overflow-{suffix}",
                Name = $"Overflow {suffix}"
            });
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.ListAsync("tenant-1"));

        Assert.Contains(nameof(IPagedRoleStore), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_claim_mapping_list_refuses_overflow_and_names_the_paged_contract()
    {
        var persistence = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.ClaimMappingStore(persistence);
        for (var index = 0; index <= IdentityStorageManifest.MaxAggregateRelationshipEntries; index++)
        {
            var suffix = index.ToString("D3");
            await store.SaveAsync(IdentityGroundworkFixtures.ClaimMappingRule() with
            {
                Id = $"claim-overflow-{suffix}",
                Order = index
            });
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.ListForProviderAsync("tenant-1", "google"));

        Assert.Contains(nameof(IPagedClaimMappingStore), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_external_identity_list_refuses_overflow_and_names_the_paged_contract()
    {
        var persistence = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.ExternalIdentityStore(persistence);
        await IdentityGroundworkFixtures.UserStore(persistence).SaveAsync(IdentityGroundworkFixtures.User());
        for (var index = 0; index < IdentityStorageManifest.MaxAggregateRelationshipEntries; index++)
        {
            var suffix = index.ToString("D3");
            await store.SaveAsync(IdentityGroundworkFixtures.ExternalIdentity() with
            {
                ProviderSubject = $"subject-overflow-{suffix}"
            });
        }

        // Admission prevents a supported caller from creating entry 513. Seed one structurally
        // valid provider row to prove a corrupted/pre-existing set cannot be silently truncated.
        var overflow = IdentityGroundworkFixtures.ExternalIdentity() with
        {
            ProviderSubject = "subject-overflow-512"
        };
        var overflowDocument = new IdentityExternalLoginDocument(
            IdentityCompositeDocumentId.Normalize(overflow.TenantId),
            IdentityCompositeDocumentId.Normalize(overflow.UserId),
            IdentityCompositeDocumentId.Normalize(overflow.Provider),
            IdentityCompositeDocumentId.Normalize(overflow.ProviderSubject),
            IdentityDocumentId.From(overflow.TenantId, overflow.Provider, overflow.ProviderSubject),
            null,
            overflow,
            IdentityDocumentId.From(overflow.TenantId, overflow.UserId));
        var seeded = persistence.Rows(IdentityGroundworkFixtures.Accessor()).Save(
            new GroundworkIdentityRowWrite(
                IdentityStorageManifest.ExternalLoginDocumentKind,
                overflowDocument.LoginKey,
                JsonSerializer.Serialize(overflowDocument, IdentityGroundworkJson.Options),
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [IdentityStorageManifest.UserLookupKeyField] = overflowDocument.UserLookupKey
                },
                GroundworkIdentityRowWriteCondition.CreateOnly));
        Assert.True(seeded.Succeeded, seeded.Message);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.ListForUserAsync("tenant-1", "user-1"));

        Assert.Contains(nameof(IPagedExternalIdentityStore), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderConfiguration_RoundTrips_Tenant_And_Global_Records()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var tenantConfiguration = IdentityGroundworkFixtures.TenantProviderConfiguration();
        var globalConfiguration = IdentityGroundworkFixtures.GlobalProviderConfiguration();

        await IdentityGroundworkFixtures.ProviderConfigurationStore(docStore).SaveAsync(tenantConfiguration);
        await IdentityGroundworkFixtures.GlobalProviderConfigurationStore(docStore).SaveAsync(globalConfiguration);

        var tenantReloaded = await IdentityGroundworkFixtures.ProviderConfigurationStore(docStore)
            .FindForTenantAsync("tenant-1", "google");
        var globalReloaded = await IdentityGroundworkFixtures.GlobalProviderConfigurationStore(docStore)
            .FindGlobalAsync("google");

        Assert.NotNull(tenantReloaded);
        Assert.Equal("tenant-1", tenantReloaded!.TenantId);
        Assert.NotNull(globalReloaded);
        Assert.Null(globalReloaded!.TenantId);
    }

    [Fact]
    public async Task ExternalIdentity_RoundTrips_By_Subject_And_Lists_For_User()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var external = IdentityGroundworkFixtures.ExternalIdentity();
        await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(IdentityGroundworkFixtures.User());

        await IdentityGroundworkFixtures.ExternalIdentityStore(docStore).SaveAsync(external);

        var store = IdentityGroundworkFixtures.ExternalIdentityStore(docStore);
        var bySubject = await store.FindBySubjectAsync("tenant-1", "google", "sub-123");
        var forUser = await store.ListForUserAsync("tenant-1", "user-1");

        Assert.NotNull(bySubject);
        Assert.Equal("user-1", bySubject!.UserId);
        Assert.Single(forUser);
    }

    [Fact]
    public async Task TenantMembership_RoundTrips_And_Survives_Restart()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var membership = IdentityGroundworkFixtures.TenantMembership();
        await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(IdentityGroundworkFixtures.User());

        await IdentityGroundworkFixtures.TenantMembershipStore(docStore).SaveAsync(membership);

        var reloaded = await IdentityGroundworkFixtures.TenantMembershipStore(docStore).FindAsync("tenant-1", "user-1");

        Assert.NotNull(reloaded);
        Assert.Equal(TenantMembershipStatus.Active, reloaded!.Status);
        Assert.Equal(membership.RoleIds, reloaded.RoleIds);
    }

    [Fact]
    public async Task Role_Save_replaces_an_existing_record_without_a_revision_contract()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.RoleStore(docStore);

        await store.SaveAsync(IdentityGroundworkFixtures.Role());
        await store.SaveAsync(IdentityGroundworkFixtures.Role() with { Name = "Renamed" });

        var list = await store.ListAsync("tenant-1");
        Assert.Equal("Renamed", Assert.Single(list).Name);
    }

    [Fact]
    public async Task Application_Save_Is_An_Upsert()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.ApplicationStore(docStore);
        var application = IdentityGroundworkFixtures.Application();

        await store.SaveAsync(application);
        await store.SaveAsync(application with { DisplayName = "Renamed Client" });

        var reloaded = await store.FindAsync(application.TenantId, application.Id);
        Assert.Equal("Renamed Client", reloaded!.DisplayName);
    }

    [Fact]
    public async Task Explicit_tenant_mismatch_fails_before_provider_io()
    {
        var source = new ThrowingSessionSource();
        var access = IdentityGroundworkFixtures.Accessor("tenant-a");
        var store = new GroundworkUserStore(new GroundworkIdentityRowStore(source, access), access);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.FindAsync("tenant-b", "user-1"));

        Assert.DoesNotContain("tenant-a", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.OpenCalls);
    }

    [Fact]
    public async Task Application_tenant_mismatch_fails_before_provider_io()
    {
        var source = new ThrowingSessionSource();
        var access = IdentityGroundworkFixtures.Accessor("tenant-a");
        var store = new GroundworkApplicationStore(new GroundworkIdentityRowStore(source, access), access);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.FindAsync("tenant-b", "app-1"));

        Assert.DoesNotContain("tenant-a", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.OpenCalls);
    }

    [Fact]
    public async Task Credential_tenant_mismatch_fails_before_provider_io()
    {
        var source = new ThrowingSessionSource();
        var access = IdentityGroundworkFixtures.Accessor("tenant-a");
        var store = new GroundworkCredentialStore(new GroundworkIdentityRowStore(source, access), access);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.FindAsync("tenant-b", "credential-1"));

        Assert.DoesNotContain("tenant-a", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.OpenCalls);
    }

    [Fact]
    public async Task ClaimMapping_tenant_mismatch_fails_before_provider_io()
    {
        var source = new ThrowingSessionSource();
        var access = IdentityGroundworkFixtures.Accessor("tenant-a");
        var store = new GroundworkClaimMappingStore(new GroundworkIdentityRowStore(source, access), access);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.ListForProviderAsync("tenant-b", "google"));

        Assert.DoesNotContain("tenant-a", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.OpenCalls);
    }

    [Fact]
    public async Task ProviderConfiguration_global_write_requires_privileged_global_access_before_provider_io()
    {
        var source = new ThrowingSessionSource();
        var access = IdentityGroundworkFixtures.Accessor("tenant-a");
        var store = new GroundworkProviderConfigurationStore(new GroundworkIdentityRowStore(source, access), access);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveAsync(IdentityGroundworkFixtures.GlobalProviderConfiguration()));

        Assert.Contains("privileged global", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.OpenCalls);
    }

    private sealed class ThrowingSessionSource : IGroundworkStorageSessionSource
    {
        private readonly IReadOnlyDictionary<string, StorageUnit> units = IdentityV2StorageManifest.CreateUnits()
            .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);

        public int OpenCalls { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCalls++;
            throw new InvalidOperationException("Provider I/O must not be reached.");
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new InvalidOperationException("Provider I/O must not be reached.");

        public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];
    }
}
