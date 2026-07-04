using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;

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
        var store = new GroundworkUserStore(docStore);
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

        await new GroundworkUserStore(docStore).SaveAsync(user);

        // Fresh store instance over the same durable document store == process restart.
        var reloaded = await new GroundworkUserStore(docStore).FindAsync(user.TenantId, user.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(user.Email, reloaded!.Email);
        Assert.Equal(user.RoleIds, reloaded.RoleIds);
    }

    [Fact]
    public async Task Role_Lists_By_Tenant_And_Survives_Restart()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var role = IdentityGroundworkFixtures.Role();

        await new GroundworkRoleStore(docStore).SaveAsync(role);

        var list = await new GroundworkRoleStore(docStore).ListAsync("tenant-1");

        Assert.Equal("role-1", Assert.Single(list).Id);
    }

    [Fact]
    public async Task ExternalIdentity_RoundTrips_By_Subject_And_Lists_For_User()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var external = IdentityGroundworkFixtures.ExternalIdentity();

        await new GroundworkExternalIdentityStore(docStore).SaveAsync(external);

        var store = new GroundworkExternalIdentityStore(docStore);
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

        await new GroundworkTenantMembershipStore(docStore).SaveAsync(membership);

        var reloaded = await new GroundworkTenantMembershipStore(docStore).FindAsync("tenant-1", "user-1");

        Assert.NotNull(reloaded);
        Assert.Equal(TenantMembershipStatus.Active, reloaded!.Status);
        Assert.Equal(membership.RoleIds, reloaded.RoleIds);
    }

    [Fact]
    public async Task Save_Is_An_Upsert()
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        var store = new GroundworkRoleStore(docStore);

        await store.SaveAsync(IdentityGroundworkFixtures.Role());
        await store.SaveAsync(IdentityGroundworkFixtures.Role() with { Name = "Renamed" });

        var list = await store.ListAsync("tenant-1");
        Assert.Equal("Renamed", Assert.Single(list).Name);
    }
}
