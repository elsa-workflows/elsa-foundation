using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>Status changes and deletes on the in-memory user store that backs hosts without EF persistence.</summary>
public sealed class InMemoryIdentityStoreTests
{
    private const string Tenant = "tenant-a";
    private readonly InMemoryIdentityStore _store = new();
    private readonly UserRecord _user = new(
        "user-a", Tenant, "Ada", "ada@example.test", "Ada", UserStatus.Active, ResourceOwnership.Foundation,
        new HashSet<string> { "role-1" }, new HashSet<string>());

    private IUserStore Users => _store;

    [Fact]
    public async Task Disable_and_enable_change_only_the_status()
    {
        await Users.SaveAsync(_user);

        Assert.True(await Users.SetStatusAsync(Tenant, _user.Id, UserStatus.Disabled));
        Assert.Equal(_user with { Status = UserStatus.Disabled }, await Users.FindAsync(Tenant, _user.Id));
        Assert.True(await Users.SetStatusAsync(Tenant, _user.Id, UserStatus.Active));
        Assert.Equal(_user, await Users.FindAsync(Tenant, _user.Id));
    }

    [Fact]
    public async Task Delete_removes_the_user_with_its_logins_and_membership()
    {
        await Users.SaveAsync(_user);
        await _store.SaveAsync(new ExternalIdentityRecord(
            Tenant, "google", "subject-a", _user.Id, DateTimeOffset.UnixEpoch, null, ExternalIdentityLinkPolicy.Auto));
        await _store.SaveAsync(new TenantMembershipRecord(
            Tenant, _user.Id, TenantMembershipStatus.Active, new HashSet<string>(), new HashSet<string>()));

        Assert.True(await Users.DeleteAsync(Tenant, _user.Id));

        Assert.Null(await Users.FindAsync(Tenant, _user.Id));
        Assert.Null(await _store.FindBySubjectAsync(Tenant, "google", "subject-a"));
        Assert.Null(await ((ITenantMembershipStore)_store).FindAsync(Tenant, _user.Id));
    }

    [Fact]
    public async Task Status_change_and_delete_report_a_missing_user()
    {
        Assert.False(await Users.SetStatusAsync(Tenant, "missing", UserStatus.Disabled));
        Assert.False(await Users.DeleteAsync(Tenant, "missing"));
    }
}
