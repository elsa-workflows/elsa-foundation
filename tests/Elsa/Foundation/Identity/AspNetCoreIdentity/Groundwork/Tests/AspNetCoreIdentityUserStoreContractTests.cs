using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityUserStoreContractTests
{
    [Fact]
    public async Task User_create_find_and_delete_round_trip_all_framework_scalar_fields()
    {
        var store = CreateStore();
        var source = AspNetCoreIdentityScenarioData.PrimaryUser;
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(source);

        var create = await store.CreateAsync(user, CancellationToken.None);
        var byId = await store.FindByIdAsync(source.Id, CancellationToken.None);
        var byName = await store.FindByNameAsync(source.NormalizedUserName, CancellationToken.None);
        var byEmail = await store.FindByEmailAsync(source.NormalizedEmail!, CancellationToken.None);

        Assert.True(create.Succeeded, ErrorText(create));
        Assert.NotNull(byId);
        Assert.Equal(source.Id, byId!.Id);
        Assert.Equal(source.TenantId, byId.TenantId);
        Assert.Equal(source.UserName, byId.UserName);
        Assert.Equal(source.NormalizedUserName, byId.NormalizedUserName);
        Assert.Equal(source.Email, byId.Email);
        Assert.Equal(source.NormalizedEmail, byId.NormalizedEmail);
        Assert.Equal(source.EmailConfirmed, byId.EmailConfirmed);
        Assert.Equal(source.PasswordHash!.Reveal(), byId.PasswordHash);
        Assert.Equal(source.SecurityStamp, byId.SecurityStamp);
        Assert.Equal(source.PhoneNumber, byId.PhoneNumber);
        Assert.Equal(source.PhoneNumberConfirmed, byId.PhoneNumberConfirmed);
        Assert.Equal(source.TwoFactorEnabled, byId.TwoFactorEnabled);
        Assert.Equal(source.LockoutEnd, byId.LockoutEnd);
        Assert.Equal(source.LockoutEnabled, byId.LockoutEnabled);
        Assert.Equal(source.AccessFailedCount, byId.AccessFailedCount);
        Assert.Equal(source.DisplayName, byId.DisplayName);
        Assert.Equal(source.Id, byName?.Id);
        Assert.Equal(source.Id, byEmail?.Id);

        var delete = await store.DeleteAsync(byId, CancellationToken.None);

        Assert.True(delete.Succeeded, ErrorText(delete));
        Assert.Null(await store.FindByIdAsync(source.Id, CancellationToken.None));
    }

    [Fact]
    public async Task User_update_persists_mutated_password_security_contact_2fa_and_lockout_state()
    {
        var store = CreateStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        Assert.True((await store.CreateAsync(user, CancellationToken.None)).Succeeded);

        await store.SetPasswordHashAsync(user, "updated-password-hash", CancellationToken.None);
        await store.SetSecurityStampAsync(user, "updated-security-stamp", CancellationToken.None);
        await store.SetEmailAsync(user, "updated@example.test", CancellationToken.None);
        await store.SetNormalizedEmailAsync(user, "UPDATED@EXAMPLE.TEST", CancellationToken.None);
        await store.SetEmailConfirmedAsync(user, false, CancellationToken.None);
        await store.SetPhoneNumberAsync(user, "+31000000999", CancellationToken.None);
        await store.SetPhoneNumberConfirmedAsync(user, false, CancellationToken.None);
        await store.SetTwoFactorEnabledAsync(user, false, CancellationToken.None);
        await store.SetLockoutEndDateAsync(user, DateTimeOffset.Parse("2042-02-03T05:05:06Z"), CancellationToken.None);
        await store.SetLockoutEnabledAsync(user, true, CancellationToken.None);
        Assert.Equal(3, await store.IncrementAccessFailedCountAsync(user, CancellationToken.None));

        var update = await store.UpdateAsync(user, CancellationToken.None);
        var reloaded = await store.FindByIdAsync(user.Id, CancellationToken.None);

        Assert.True(update.Succeeded, ErrorText(update));
        Assert.NotNull(reloaded);
        Assert.Equal("updated-password-hash", await store.GetPasswordHashAsync(reloaded!, CancellationToken.None));
        Assert.True(await store.HasPasswordAsync(reloaded, CancellationToken.None));
        Assert.Equal("updated-security-stamp", await store.GetSecurityStampAsync(reloaded, CancellationToken.None));
        Assert.Equal("updated@example.test", await store.GetEmailAsync(reloaded, CancellationToken.None));
        Assert.Equal("UPDATED@EXAMPLE.TEST", await store.GetNormalizedEmailAsync(reloaded, CancellationToken.None));
        Assert.False(await store.GetEmailConfirmedAsync(reloaded, CancellationToken.None));
        Assert.Equal("+31000000999", await store.GetPhoneNumberAsync(reloaded, CancellationToken.None));
        Assert.False(await store.GetPhoneNumberConfirmedAsync(reloaded, CancellationToken.None));
        Assert.False(await store.GetTwoFactorEnabledAsync(reloaded, CancellationToken.None));
        Assert.Equal(DateTimeOffset.Parse("2042-02-03T05:05:06Z"), await store.GetLockoutEndDateAsync(reloaded, CancellationToken.None));
        Assert.True(await store.GetLockoutEnabledAsync(reloaded, CancellationToken.None));
        Assert.Equal(3, await store.GetAccessFailedCountAsync(reloaded, CancellationToken.None));

        await store.ResetAccessFailedCountAsync(reloaded, CancellationToken.None);
        Assert.True((await store.UpdateAsync(reloaded, CancellationToken.None)).Succeeded);
        Assert.Equal(0, await store.GetAccessFailedCountAsync((await store.FindByIdAsync(user.Id, CancellationToken.None))!, CancellationToken.None));
    }

    private static GroundworkIdentityUserStore CreateStore() =>
        new AspNetCoreIdentityGroundworkStoreFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant).UserStore();

    private static string ErrorText(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
}
