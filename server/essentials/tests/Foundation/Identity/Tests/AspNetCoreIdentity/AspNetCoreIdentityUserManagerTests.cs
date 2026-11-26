using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>Disabling a user through <see cref="IUserManager"/> changes its status and nothing else (#2774).</summary>
public sealed class AspNetCoreIdentityUserManagerTests
{
    private const string Tenant = "tenant-a";
    private readonly InMemoryIdentityStore _store = new();
    private readonly UserRecord _user = new(
        "user-a", Tenant, "Ada", "ada@example.test", "Ada", UserStatus.Active, ResourceOwnership.Foundation,
        new HashSet<string> { "role-1" }, new HashSet<string>());

    [Fact]
    public async Task Disable_keeps_a_role_change_made_between_a_read_and_the_disable()
    {
        await ((IUserStore)_store).SaveAsync(_user);
        var promoted = _user with { RoleIds = new HashSet<string> { "role-1", "role-admin" } };
        var manager = new AspNetCoreIdentityUserManager(new ConcurrentRoleChangeUserStore(_store, promoted), _store);

        await manager.DisableAsync(Tenant, _user.Id);

        var stored = Assert.IsType<UserRecord>(await ((IUserStore)_store).FindAsync(Tenant, _user.Id));
        Assert.Equal(UserStatus.Disabled, stored.Status);
        Assert.Equal(["role-1", "role-admin"], stored.RoleIds.Order());
    }

    [Fact]
    public async Task Disable_refuses_a_missing_user()
    {
        var manager = new AspNetCoreIdentityUserManager(_store, _store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DisableAsync(Tenant, "missing").AsTask());
    }

    /// <summary>
    /// Saves <paramref name="changed"/> once, as another writer would, on the manager's first store call: right after
    /// a read returns, or right before a status write runs.
    /// </summary>
    private sealed class ConcurrentRoleChangeUserStore(IUserStore inner, UserRecord changed) : IUserStore
    {
        private bool _changed;

        public async ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
        {
            var user = await inner.FindAsync(tenantId, userId, cancellationToken);
            await ChangeOnceAsync(cancellationToken);
            return user;
        }

        public async ValueTask<bool> SetStatusAsync(string tenantId, string userId, UserStatus status, CancellationToken cancellationToken = default)
        {
            await ChangeOnceAsync(cancellationToken);
            return await inner.SetStatusAsync(tenantId, userId, status, cancellationToken);
        }

        public ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default) =>
            inner.FindByEmailAsync(tenantId, email, cancellationToken);

        public ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default) => inner.SaveAsync(user, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, string userId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(tenantId, userId, cancellationToken);

        private async ValueTask ChangeOnceAsync(CancellationToken cancellationToken)
        {
            if (_changed)
                return;
            _changed = true;
            await inner.SaveAsync(changed, cancellationToken);
        }
    }
}
