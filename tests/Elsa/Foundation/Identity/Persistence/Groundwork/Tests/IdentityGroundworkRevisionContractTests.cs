using Elsa.Foundation.Identity.Abstractions.Iam;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

public sealed class IdentityGroundworkRevisionContractTests
{
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
