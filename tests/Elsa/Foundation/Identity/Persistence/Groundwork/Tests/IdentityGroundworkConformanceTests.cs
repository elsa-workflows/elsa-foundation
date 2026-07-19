using Elsa.Foundation.Identity.Abstractions.Iam;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

/// <summary>Shared direct coverage for the local IAM stores that are not owned by the #644 authority adapter.</summary>
public sealed class IdentityGroundworkConformanceTests
{
    [Fact]
    public async Task Application_and_credential_identities_are_tenant_local_and_survive_a_store_reopen()
    {
        var documents = IdentityGroundworkFixtures.NewDocumentStore();
        var firstApplication = IdentityGroundworkFixtures.Application();
        var secondApplication = firstApplication with { TenantId = "tenant-2", ClientId = "client-2" };
        var firstCredential = IdentityGroundworkFixtures.Credential();
        var secondCredential = firstCredential with { TenantId = "tenant-2", SubjectId = "app-2" };

        await IdentityGroundworkFixtures.ApplicationStore(documents).SaveAsync(firstApplication);
        await IdentityGroundworkFixtures.ApplicationStore(documents, "tenant-2").SaveAsync(secondApplication);
        await IdentityGroundworkFixtures.CredentialStore(documents).SaveAsync(firstCredential);
        await IdentityGroundworkFixtures.CredentialStore(documents, "tenant-2").SaveAsync(secondCredential);

        var reopenedFirstApplications = IdentityGroundworkFixtures.ApplicationStore(documents);
        var reopenedSecondApplications = IdentityGroundworkFixtures.ApplicationStore(documents, "tenant-2");
        var reopenedFirstCredentials = IdentityGroundworkFixtures.CredentialStore(documents);
        var reopenedSecondCredentials = IdentityGroundworkFixtures.CredentialStore(documents, "tenant-2");

        Assert.Equal("client-1", (await reopenedFirstApplications.FindAsync("tenant-1", firstApplication.Id))!.ClientId);
        Assert.Equal("client-2", (await reopenedSecondApplications.FindAsync("tenant-2", secondApplication.Id))!.ClientId);
        Assert.Equal("app-1", (await reopenedFirstCredentials.FindAsync("tenant-1", firstCredential.Id))!.SubjectId);
        Assert.Equal("app-2", (await reopenedSecondCredentials.FindAsync("tenant-2", secondCredential.Id))!.SubjectId);
    }

    [Fact]
    public async Task Claim_mapping_identity_is_tenant_local_and_survives_a_store_reopen()
    {
        var documents = IdentityGroundworkFixtures.NewDocumentStore();
        var firstRule = IdentityGroundworkFixtures.ClaimMappingRule();
        var secondRule = firstRule with { TenantId = "tenant-2", Order = 20 };

        await IdentityGroundworkFixtures.ClaimMappingStore(documents).SaveAsync(firstRule);
        await IdentityGroundworkFixtures.ClaimMappingStore(documents, "tenant-2").SaveAsync(secondRule);

        var firstRules = await IdentityGroundworkFixtures.ClaimMappingStore(documents)
            .ListForProviderAsync("tenant-1", firstRule.Provider);
        var secondRules = await IdentityGroundworkFixtures.ClaimMappingStore(documents, "tenant-2")
            .ListForProviderAsync("tenant-2", secondRule.Provider);

        Assert.Equal(10, Assert.Single(firstRules).Order);
        Assert.Equal(20, Assert.Single(secondRules).Order);
    }

    [Fact]
    public async Task Provider_configuration_keeps_tenant_collisions_separate_from_the_privileged_global_record()
    {
        var documents = IdentityGroundworkFixtures.NewDocumentStore();
        var firstTenant = IdentityGroundworkFixtures.TenantProviderConfiguration();
        var secondTenant = firstTenant with { TenantId = "tenant-2", Kind = "tenant-two-oidc" };
        var global = IdentityGroundworkFixtures.GlobalProviderConfiguration();

        var firstTenantStore = IdentityGroundworkFixtures.ProviderConfigurationStore(documents);
        await firstTenantStore.SaveAsync(firstTenant);
        await IdentityGroundworkFixtures.ProviderConfigurationStore(documents, "tenant-2").SaveAsync(secondTenant);
        await IdentityGroundworkFixtures.GlobalProviderConfigurationStore(documents).SaveAsync(global);

        var duplicate = await Assert.IsAssignableFrom<IRevisionAwareProviderConfigurationStore>(firstTenantStore)
            .SaveWithRevisionAsync(firstTenant with { Kind = "duplicate-tenant-one-oidc" }, expectedRevision: null);

        var reopenedFirst = IdentityGroundworkFixtures.ProviderConfigurationStore(documents);
        var reopenedSecond = IdentityGroundworkFixtures.ProviderConfigurationStore(documents, "tenant-2");
        var reopenedGlobal = IdentityGroundworkFixtures.GlobalProviderConfigurationStore(documents);

        Assert.Equal("external-oidc", (await reopenedFirst.FindForTenantAsync("tenant-1", firstTenant.Provider))!.Kind);
        Assert.Equal("tenant-two-oidc", (await reopenedSecond.FindForTenantAsync("tenant-2", secondTenant.Provider))!.Kind);
        Assert.Null((await reopenedGlobal.FindGlobalAsync(global.Provider))!.TenantId);
        Assert.Equal(IamRevisionSaveStatus.Conflict, duplicate.Status);
    }
}
