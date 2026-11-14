using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

public sealed partial class IdentityAuthorityEntityFrameworkCoreBehaviorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disable_and_reenable_advance_the_private_epoch_without_rewriting_saved_authority(bool saveRecord)
    {
        var path = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(path, "tenant-a");
            await scope.Roles.SaveAsync(Role("tenant-a", "reader", "Reader"));
            var user = User("tenant-a", "epoch-user", "Epoch User", "epoch@example.test") with
            {
                RoleIds = Set("reader"), DirectPermissions = Set("workflows:read")
            };
            await scope.Users.SaveAsync(user);
            var initial = await Epoch();
            Assert.False(string.IsNullOrWhiteSpace(initial));

            await Change(UserStatus.Disabled);
            var disabled = await Epoch();
            Assert.False(string.IsNullOrWhiteSpace(disabled));
            Assert.NotEqual(initial, disabled);
            AssertUser(user with { Status = UserStatus.Disabled },
                Assert.IsType<UserRecord>(await scope.Users.FindAsync(user.TenantId, user.Id)));

            await Change(UserStatus.Active);
            var reenabled = await Epoch();
            Assert.False(string.IsNullOrWhiteSpace(reenabled));
            Assert.NotEqual(initial, reenabled);
            Assert.NotEqual(disabled, reenabled);
            AssertUser(user, Assert.IsType<UserRecord>(await scope.Users.FindAsync(user.TenantId, user.Id)));

            // Repeating the current status is not another credential transition.
            await Change(UserStatus.Active);
            Assert.Equal(reenabled, await Epoch());

            async Task Change(UserStatus status)
            {
                if (saveRecord)
                {
                    var current = (await scope.Users.FindAsync(user.TenantId, user.Id))!;
                    await scope.Users.SaveAsync(current with { Status = status });
                }
                else
                    Assert.True(await scope.Users.SetStatusAsync(user.TenantId, user.Id, status));
            }

            async Task<string?> Epoch() =>
                (await scope.Context.Users.AsNoTracking().SingleAsync(row => row.UserId == user.Id)).SecurityStamp;
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }
}
