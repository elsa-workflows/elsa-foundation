using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// Round-trip behaviour of the EF Core-backed Elsa IAM stores over a private in-memory
/// <see cref="ApplicationIdentityDbContext"/>. Each test gets its own context via the shared
/// <see cref="NewContext"/> helper so there is no repeated arrange boilerplate.
/// </summary>
public sealed class EfCoreIdentityStoreTests : IDisposable
{
    private readonly string _databaseName = $"store-{Guid.NewGuid():n}";
    private readonly List<ApplicationIdentityDbContext> _contexts = [];

    private ApplicationIdentityDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationIdentityDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;

        var context = new ApplicationIdentityDbContext(options);
        context.Database.EnsureCreated();
        _contexts.Add(context);
        return context;
    }

    [Fact]
    public async Task User_RoundTrips_By_Id_And_Email_With_Roles_And_Permissions()
    {
        var user = new UserRecord(
            "user-1", "tenant-a", "alice", "alice@example.com", "Alice",
            UserStatus.Active, ResourceOwnership.Foundation,
            new HashSet<string> { "role-1" },
            new HashSet<string> { "identity.users.read" });

        await new EfCoreUserStore(NewContext()).SaveAsync(user);

        var store = new EfCoreUserStore(NewContext());
        var byId = await store.FindAsync("tenant-a", "user-1");
        var byEmail = await store.FindByEmailAsync("tenant-a", "ALICE@example.com");

        Assert.NotNull(byId);
        Assert.Equal("alice", byId!.UserName);
        Assert.Contains("role-1", byId.RoleIds);
        Assert.Contains("identity.users.read", byId.DirectPermissions);
        Assert.NotNull(byEmail);
        Assert.Equal("user-1", byEmail!.Id);
    }

    [Fact]
    public async Task User_Save_Is_An_Upsert()
    {
        var user = new UserRecord(
            "user-2", "tenant-a", "bob", "bob@example.com", "Bob",
            UserStatus.Active, ResourceOwnership.Foundation, new HashSet<string>(), new HashSet<string>());

        await new EfCoreUserStore(NewContext()).SaveAsync(user);
        await new EfCoreUserStore(NewContext()).SaveAsync(user with { DisplayName = "Bobby", DirectPermissions = new HashSet<string> { "identity.roles.read" } });

        var reloaded = await new EfCoreUserStore(NewContext()).FindAsync("tenant-a", "user-2");
        Assert.Equal("Bobby", reloaded!.DisplayName);
        Assert.Single(reloaded.DirectPermissions);
        Assert.Contains("identity.roles.read", reloaded.DirectPermissions);
    }

    [Fact]
    public async Task Role_RoundTrips_By_Id_And_Lists_By_Tenant()
    {
        var role = new RoleRecord("role-1", "tenant-a", "Editor", null, new HashSet<string> { "identity.users.manage" }, System: false);

        await new EfCoreRoleStore(NewContext()).SaveAsync(role);

        var store = new EfCoreRoleStore(NewContext());
        var byId = await store.FindAsync("tenant-a", "role-1");
        var list = await store.ListAsync("tenant-a");
        var crossTenant = await store.FindAsync("tenant-b", "role-1");

        Assert.NotNull(byId);
        Assert.Equal("Editor", byId!.Name);
        Assert.Contains("identity.users.manage", byId.Permissions);
        Assert.Equal("role-1", Assert.Single(list).Id);
        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task ExternalIdentity_RoundTrips_By_Subject_And_Lists_For_User()
    {
        // The external-identity store resolves tenancy via the owning user, so seed a user first.
        await new EfCoreUserStore(NewContext()).SaveAsync(new UserRecord(
            "user-3", "tenant-a", "carol", null, null, UserStatus.Active, ResourceOwnership.Foundation,
            new HashSet<string>(), new HashSet<string>()));

        await new EfCoreExternalIdentityStore(NewContext()).SaveAsync(new ExternalIdentityRecord(
            "tenant-a", "google", "sub-123", "user-3", DateTimeOffset.UnixEpoch, null, ExternalIdentityLinkPolicy.Auto));

        var store = new EfCoreExternalIdentityStore(NewContext());
        var bySubject = await store.FindBySubjectAsync("tenant-a", "google", "sub-123");
        var forUser = await store.ListForUserAsync("tenant-a", "user-3");

        Assert.NotNull(bySubject);
        Assert.Equal("user-3", bySubject!.UserId);
        Assert.Single(forUser);
    }

    [Fact]
    public async Task TenantMembership_RoundTrips()
    {
        var membership = new TenantMembershipRecord(
            "tenant-a", "user-4", TenantMembershipStatus.Active,
            new HashSet<string> { "role-1" }, new HashSet<string> { "identity.users.read" });

        await new EfCoreTenantMembershipStore(NewContext()).SaveAsync(membership);

        var reloaded = await new EfCoreTenantMembershipStore(NewContext()).FindAsync("tenant-a", "user-4");

        Assert.NotNull(reloaded);
        Assert.Equal(TenantMembershipStatus.Active, reloaded!.Status);
        Assert.Contains("role-1", reloaded.RoleIds);
        Assert.Contains("identity.users.read", reloaded.DirectPermissions);
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
            context.Dispose();
    }
}
