using Elsa.Foundation.Identity.Abstractions.Iam;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

public sealed class IdentityGroundworkRevisionContractTests
{
    [Fact]
    public async Task Application_revision_save_rejects_stale_revision_without_mutating_state()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.ApplicationStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareApplicationStore>(store);
        var application = IdentityGroundworkFixtures.Application();
        var created = await revisionAware.SaveWithRevisionAsync(application, expectedRevision: null);
        var duplicate = await revisionAware.SaveWithRevisionAsync(
            application with { DisplayName = "Duplicate client" },
            expectedRevision: null);

        var first = await revisionAware.FindWithRevisionAsync(application.TenantId, application.Id);
        var second = await revisionAware.FindWithRevisionAsync(application.TenantId, application.Id);

        Assert.Equal(IamRevisionSaveStatus.Saved, created.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, duplicate.Status);
        Assert.NotNull(first);
        Assert.NotNull(second);
        var saved = await revisionAware.SaveWithRevisionAsync(
            first.Record with { DisplayName = "Updated client" },
            first.Revision);
        var stale = await revisionAware.SaveWithRevisionAsync(
            second.Record with { DisplayName = "Stale client" },
            second.Revision);

        var reloaded = await store.FindAsync(application.TenantId, application.Id);
        Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, stale.Status);
        Assert.Equal("Updated client", reloaded!.DisplayName);
        Assert.NotEqual(first.Revision, saved.Revision);
    }

    [Fact]
    public async Task Credential_revision_save_rejects_stale_revision_without_mutating_state()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.CredentialStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareCredentialStore>(store);
        var credential = IdentityGroundworkFixtures.Credential();
        var created = await revisionAware.SaveWithRevisionAsync(credential, expectedRevision: null);
        var duplicate = await revisionAware.SaveWithRevisionAsync(
            credential with { Status = CredentialStatus.Revoked },
            expectedRevision: null);

        var first = await revisionAware.FindWithRevisionAsync(credential.TenantId, credential.Id);
        var second = await revisionAware.FindWithRevisionAsync(credential.TenantId, credential.Id);

        Assert.Equal(IamRevisionSaveStatus.Saved, created.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, duplicate.Status);
        Assert.NotNull(first);
        Assert.NotNull(second);
        var saved = await revisionAware.SaveWithRevisionAsync(
            first.Record with { Status = CredentialStatus.Revoked },
            first.Revision);
        var stale = await revisionAware.SaveWithRevisionAsync(
            second.Record with { Status = CredentialStatus.Active },
            second.Revision);

        var reloaded = await store.FindAsync(credential.TenantId, credential.Id);
        Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, stale.Status);
        Assert.Equal(CredentialStatus.Revoked, reloaded!.Status);
        Assert.NotEqual(first.Revision, saved.Revision);
    }

    [Fact]
    public async Task User_revision_save_rejects_stale_revision_without_mutating_state()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.UserStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareUserStore>(store);
        var user = IdentityGroundworkFixtures.User();
        await store.SaveAsync(user);

        var first = await revisionAware.FindWithRevisionAsync(user.TenantId, user.Id);
        var second = await revisionAware.FindWithRevisionAsync(user.TenantId, user.Id);

        Assert.NotNull(first);
        Assert.NotNull(second);
        var saved = await revisionAware.SaveWithRevisionAsync(
            first.Record with { DisplayName = "Alice Updated" },
            first.Revision);
        var stale = await revisionAware.SaveWithRevisionAsync(
            second.Record with { DisplayName = "Alice Stale" },
            second.Revision);

        var reloaded = await store.FindAsync(user.TenantId, user.Id);
        Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, stale.Status);
        Assert.Equal("Alice Updated", reloaded!.DisplayName);
        Assert.NotEqual(first.Revision, saved.Revision);
    }

    [Fact]
    public async Task Role_revision_save_rejects_stale_revision_without_mutating_state()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.RoleStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareRoleStore>(store);
        var role = IdentityGroundworkFixtures.Role();
        await store.SaveAsync(role);

        var first = await revisionAware.FindWithRevisionAsync(role.TenantId, role.Id);
        var second = await revisionAware.FindWithRevisionAsync(role.TenantId, role.Id);

        Assert.NotNull(first);
        Assert.NotNull(second);
        var saved = await revisionAware.SaveWithRevisionAsync(
            first.Record with { Description = "Updated access" },
            first.Revision);
        var stale = await revisionAware.SaveWithRevisionAsync(
            second.Record with { Description = "Stale access" },
            second.Revision);

        var reloaded = await store.FindAsync(role.TenantId, role.Id);
        Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, stale.Status);
        Assert.Equal("Updated access", reloaded!.Description);
        Assert.NotEqual(first.Revision, saved.Revision);
    }

    [Fact]
    public async Task External_identity_revision_save_rejects_stale_revision_without_mutating_state()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.ExternalIdentityStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareExternalIdentityStore>(store);
        var externalIdentity = IdentityGroundworkFixtures.ExternalIdentity();
        await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(IdentityGroundworkFixtures.User());
        await store.SaveAsync(externalIdentity);

        var first = await revisionAware.FindBySubjectWithRevisionAsync(
            externalIdentity.TenantId,
            externalIdentity.Provider,
            externalIdentity.ProviderSubject);
        var second = await revisionAware.FindBySubjectWithRevisionAsync(
            externalIdentity.TenantId,
            externalIdentity.Provider,
            externalIdentity.ProviderSubject);

        Assert.NotNull(first);
        Assert.NotNull(second);
        var saved = await revisionAware.SaveWithRevisionAsync(
            first.Record with { LastSeenAt = DateTimeOffset.UnixEpoch.AddMinutes(1) },
            first.Revision);
        var stale = await revisionAware.SaveWithRevisionAsync(
            second.Record with { LastSeenAt = DateTimeOffset.UnixEpoch.AddMinutes(2) },
            second.Revision);

        var reloaded = await store.FindBySubjectAsync(
            externalIdentity.TenantId,
            externalIdentity.Provider,
            externalIdentity.ProviderSubject);
        Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, stale.Status);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(1), reloaded!.LastSeenAt);
        Assert.NotEqual(first.Revision, saved.Revision);
    }

    [Fact]
    public async Task External_identity_revision_save_can_explicitly_rebind_to_another_existing_user()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var userStore = IdentityGroundworkFixtures.UserStore(docStore);
        var store = IdentityGroundworkFixtures.ExternalIdentityStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareExternalIdentityStore>(store);
        var originalOwner = IdentityGroundworkFixtures.User();
        var newOwner = originalOwner with
        {
            Id = "user-2",
            UserName = "bob",
            Email = "bob@example.com",
            DisplayName = "Bob Example"
        };
        var externalIdentity = IdentityGroundworkFixtures.ExternalIdentity();
        await userStore.SaveAsync(originalOwner);
        await userStore.SaveAsync(newOwner);
        await store.SaveAsync(externalIdentity);
        var current = await revisionAware.FindBySubjectWithRevisionAsync(
            externalIdentity.TenantId,
            externalIdentity.Provider,
            externalIdentity.ProviderSubject);
        Assert.NotNull(current);

        var rebound = await revisionAware.SaveWithRevisionAsync(
            current.Record with { UserId = newOwner.Id },
            current.Revision);

        Assert.Equal(IamRevisionSaveStatus.Saved, rebound.Status);
        Assert.Empty(await store.ListForUserAsync(originalOwner.TenantId, originalOwner.Id));
        Assert.Equal(newOwner.Id, Assert.Single(await store.ListForUserAsync(newOwner.TenantId, newOwner.Id)).UserId);
    }

    [Fact]
    public async Task Claim_mapping_revision_save_rejects_stale_revision_without_mutating_state()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.ClaimMappingStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareClaimMappingStore>(store);
        var rule = IdentityGroundworkFixtures.ClaimMappingRule();
        await store.SaveAsync(rule);

        var first = await revisionAware.FindWithRevisionAsync(rule.TenantId, rule.Provider, rule.Id);
        var second = await revisionAware.FindWithRevisionAsync(rule.TenantId, rule.Provider, rule.Id);

        Assert.NotNull(first);
        Assert.NotNull(second);
        var saved = await revisionAware.SaveWithRevisionAsync(
            first.Record with { Order = 20 },
            first.Revision);
        var stale = await revisionAware.SaveWithRevisionAsync(
            second.Record with { Order = 30 },
            second.Revision);

        var reloaded = await store.ListForProviderAsync(rule.TenantId, rule.Provider);
        Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, stale.Status);
        Assert.Equal(20, Assert.Single(reloaded).Order);
        Assert.NotEqual(first.Revision, saved.Revision);
    }

    [Fact]
    public async Task Claim_mapping_revision_save_null_revision_is_create_only()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.ClaimMappingStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareClaimMappingStore>(store);
        var rule = IdentityGroundworkFixtures.ClaimMappingRule();

        var created = await revisionAware.SaveWithRevisionAsync(rule, expectedRevision: null);
        var duplicate = await revisionAware.SaveWithRevisionAsync(rule with { Order = 99 }, expectedRevision: null);

        Assert.Equal(IamRevisionSaveStatus.Saved, created.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, duplicate.Status);
        Assert.Equal(10, Assert.Single(await store.ListForProviderAsync(rule.TenantId, rule.Provider)).Order);
    }

    [Fact]
    public async Task Provider_configuration_revision_save_rejects_stale_tenant_revision_without_mutating_state()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.ProviderConfigurationStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareProviderConfigurationStore>(store);
        var configuration = IdentityGroundworkFixtures.TenantProviderConfiguration();
        await store.SaveAsync(configuration);

        var first = await revisionAware.FindForTenantWithRevisionAsync(configuration.TenantId!, configuration.Provider);
        var second = await revisionAware.FindForTenantWithRevisionAsync(configuration.TenantId!, configuration.Provider);

        Assert.NotNull(first);
        Assert.NotNull(second);
        var saved = await revisionAware.SaveWithRevisionAsync(
            first.Record with { Enabled = false },
            first.Revision);
        var stale = await revisionAware.SaveWithRevisionAsync(
            second.Record with { Kind = "stale-kind" },
            second.Revision);

        var reloaded = await store.FindForTenantAsync(configuration.TenantId!, configuration.Provider);
        Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, stale.Status);
        Assert.False(reloaded!.Enabled);
        Assert.Equal(configuration.Kind, reloaded.Kind);
        Assert.NotEqual(first.Revision, saved.Revision);
    }

    [Fact]
    public async Task Provider_configuration_revision_save_rejects_stale_global_revision_without_mutating_state()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.GlobalProviderConfigurationStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareProviderConfigurationStore>(store);
        var configuration = IdentityGroundworkFixtures.GlobalProviderConfiguration();
        await store.SaveAsync(configuration);

        var first = await revisionAware.FindGlobalWithRevisionAsync(configuration.Provider);
        var second = await revisionAware.FindGlobalWithRevisionAsync(configuration.Provider);

        Assert.NotNull(first);
        Assert.NotNull(second);
        var saved = await revisionAware.SaveWithRevisionAsync(
            first.Record with { Enabled = false },
            first.Revision);
        var stale = await revisionAware.SaveWithRevisionAsync(
            second.Record with { Kind = "stale-kind" },
            second.Revision);

        var reloaded = await store.FindGlobalAsync(configuration.Provider);
        Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, stale.Status);
        Assert.False(reloaded!.Enabled);
        Assert.Equal(configuration.Kind, reloaded.Kind);
        Assert.NotEqual(first.Revision, saved.Revision);
    }

    [Fact]
    public async Task Tenant_membership_revision_save_rejects_stale_revision_without_mutating_state()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = IdentityGroundworkFixtures.TenantMembershipStore(docStore);
        var revisionAware = Assert.IsAssignableFrom<IRevisionAwareTenantMembershipStore>(store);
        var membership = IdentityGroundworkFixtures.TenantMembership();
        await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(IdentityGroundworkFixtures.User());
        await store.SaveAsync(membership);

        var first = await revisionAware.FindWithRevisionAsync(membership.TenantId, membership.UserId);
        var second = await revisionAware.FindWithRevisionAsync(membership.TenantId, membership.UserId);

        Assert.NotNull(first);
        Assert.NotNull(second);
        var saved = await revisionAware.SaveWithRevisionAsync(
            first.Record with { Status = TenantMembershipStatus.Suspended },
            first.Revision);
        var stale = await revisionAware.SaveWithRevisionAsync(
            second.Record with { Status = TenantMembershipStatus.Active },
            second.Revision);

        var reloaded = await store.FindAsync(membership.TenantId, membership.UserId);
        Assert.Equal(IamRevisionSaveStatus.Saved, saved.Status);
        Assert.Equal(IamRevisionSaveStatus.Conflict, stale.Status);
        Assert.Equal(TenantMembershipStatus.Suspended, reloaded!.Status);
        Assert.NotEqual(first.Revision, saved.Revision);
    }
}
