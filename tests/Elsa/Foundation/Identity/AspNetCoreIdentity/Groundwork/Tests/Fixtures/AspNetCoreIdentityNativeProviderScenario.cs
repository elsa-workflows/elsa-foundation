using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Groundwork.Store;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;

internal static class AspNetCoreIdentityNativeProviderScenario
{
    private const string Password = "Correct1!";

    public static async Task RunAsync(
        string providerKey,
        Func<IStorageProviderConnection> createConnection)
    {
        using (var connection = createConnection())
        {
            await RunFrameworkCapabilitiesAsync(connection);
            await RunTenantAndAtomicityAsync(connection);
            if (providerKey == "sqlite")
                await RunSharedConnectionConcurrencyAsync(connection);
        }
        if (providerKey != "sqlite")
            await RunIndependentClientConcurrencyAsync(createConnection);
    }

    public static async Task RunFrameworkCapabilitiesAsync(
        IStorageProviderConnection? connection = null)
    {
        await using var fixture = AspNetCoreIdentityGroundworkAcceptanceFixture.Create(connection);
        await using var scope = fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = CreateUser("manager-user", "manager-ada", "manager-ada@example.test");
        var role = new IdentityRole
        {
            Id = "manager-role",
            Name = "manager-admin",
            ConcurrencyStamp = "role-revision-v1"
        };
        var userClaim = new Claim("manager-capability", "before");
        var replacementClaim = new Claim("manager-capability", "after");
        var roleClaim = new Claim("manager-role-capability", "granted");
        var login = new UserLoginInfo("manager-provider", "manager-subject", "Manager Provider");

        RequireSucceeded(await roles.CreateAsync(role));
        RequireSucceeded(await users.CreateAsync(user, Password));

        Assert.True(await users.HasPasswordAsync(user));
        Assert.True(await users.CheckPasswordAsync(user, Password));
        RequireSucceeded(await users.ChangePasswordAsync(user, Password, "Changed1!"));
        Assert.True(await users.CheckPasswordAsync(user, "Changed1!"));

        var originalSecurityStamp = await users.GetSecurityStampAsync(user);
        RequireSucceeded(await users.UpdateSecurityStampAsync(user));
        Assert.NotEqual(originalSecurityStamp, await users.GetSecurityStampAsync(user));

        RequireSucceeded(await users.SetEmailAsync(user, "manager-updated@example.test"));
        Assert.Equal("manager-updated@example.test", await users.GetEmailAsync(user));
        Assert.Equal(user.Id, (await users.FindByEmailAsync("manager-updated@example.test"))?.Id);

        RequireSucceeded(await users.SetPhoneNumberAsync(user, "+31000000002"));
        Assert.Equal("+31000000002", await users.GetPhoneNumberAsync(user));

        RequireSucceeded(await users.SetLockoutEnabledAsync(user, true));
        Assert.True(await users.GetLockoutEnabledAsync(user));
        RequireSucceeded(await users.AccessFailedAsync(user));
        Assert.Equal(1, await users.GetAccessFailedCountAsync(user));
        RequireSucceeded(await users.ResetAccessFailedCountAsync(user));
        Assert.Equal(0, await users.GetAccessFailedCountAsync(user));
        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(5);
        RequireSucceeded(await users.SetLockoutEndDateAsync(user, lockoutEnd));
        Assert.Equal(lockoutEnd, await users.GetLockoutEndDateAsync(user));

        RequireSucceeded(await users.SetTwoFactorEnabledAsync(user, true));
        Assert.True(await users.GetTwoFactorEnabledAsync(user));

        RequireSucceeded(await users.AddLoginAsync(user, login));
        Assert.Contains(await users.GetLoginsAsync(user), candidate =>
            candidate.LoginProvider == login.LoginProvider && candidate.ProviderKey == login.ProviderKey);
        Assert.Equal(user.Id, (await users.FindByLoginAsync(login.LoginProvider, login.ProviderKey))?.Id);

        RequireSucceeded(await users.AddClaimAsync(user, userClaim));
        Assert.Contains(await users.GetClaimsAsync(user), candidate =>
            candidate.Type == userClaim.Type && candidate.Value == userClaim.Value);
        Assert.Contains(await users.GetUsersForClaimAsync(userClaim), candidate => candidate.Id == user.Id);
        RequireSucceeded(await users.ReplaceClaimAsync(user, userClaim, replacementClaim));
        Assert.Contains(await users.GetClaimsAsync(user), candidate =>
            candidate.Type == replacementClaim.Type && candidate.Value == replacementClaim.Value);

        RequireSucceeded(await roles.AddClaimAsync(role, roleClaim));
        Assert.Contains(await roles.GetClaimsAsync(role), candidate =>
            candidate.Type == roleClaim.Type && candidate.Value == roleClaim.Value);

        RequireSucceeded(await users.AddToRoleAsync(user, role.Name!));
        Assert.True(await users.IsInRoleAsync(user, role.Name!));
        Assert.Equal([role.Name], await users.GetRolesAsync(user));
        Assert.Contains(await users.GetUsersInRoleAsync(role.Name!), candidate => candidate.Id == user.Id);

        RequireSucceeded(await users.SetAuthenticationTokenAsync(user, "manager-provider", "refresh", "refresh-v1"));
        Assert.Equal("refresh-v1", await users.GetAuthenticationTokenAsync(user, "manager-provider", "refresh"));
        RequireSucceeded(await users.RemoveAuthenticationTokenAsync(user, "manager-provider", "refresh"));
        Assert.Null(await users.GetAuthenticationTokenAsync(user, "manager-provider", "refresh"));

        RequireSucceeded(await users.ResetAuthenticatorKeyAsync(user));
        Assert.False(string.IsNullOrWhiteSpace(await users.GetAuthenticatorKeyAsync(user)));
        var recoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 2))!.ToArray();
        Assert.Equal(2, recoveryCodes.Length);
        Assert.Equal(2, await users.CountRecoveryCodesAsync(user));
        RequireSucceeded(await users.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCodes[0]));
        Assert.Equal(1, await users.CountRecoveryCodesAsync(user));

        RequireSucceeded(await users.RemoveFromRoleAsync(user, role.Name!));
        Assert.False(await users.IsInRoleAsync(user, role.Name!));
        RequireSucceeded(await users.RemoveClaimAsync(user, replacementClaim));
        Assert.DoesNotContain(await users.GetClaimsAsync(user), candidate => candidate.Type == replacementClaim.Type);
        RequireSucceeded(await users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey));
        Assert.DoesNotContain(await users.GetLoginsAsync(user), candidate => candidate.LoginProvider == login.LoginProvider);

        user.DisplayName = "Manager Ada Updated";
        RequireSucceeded(await users.UpdateAsync(user));
        Assert.Equal("Manager Ada Updated", (await users.FindByIdAsync(user.Id))?.DisplayName);
        Assert.Equal(user.Id, (await users.FindByNameAsync(user.UserName!))?.Id);
        Assert.Equal(role.Id, (await roles.FindByIdAsync(role.Id))?.Id);
        Assert.Equal(role.Id, (await roles.FindByNameAsync(role.Name!))?.Id);
    }

    private static async Task RunTenantAndAtomicityAsync(
        IStorageProviderConnection connection)
    {
        using var persistence = new AspNetCoreIdentityTestPersistence(
            new NonDisposingStorageProviderConnection(connection));
        var tenantA = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence,
            requireUniqueEmail: true);
        var tenantB = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.SecondaryTenant,
            persistence,
            requireUniqueEmail: true);
        var tenantAStore = tenantA.UserStore();
        var tenantBStore = tenantB.UserStore();
        var userA = CreateUser("native-user-a", "shared-native", "shared-native@example.test");
        var userB = CreateUser("native-user-b", "shared-native", "shared-native@example.test");
        userB.TenantId = AspNetCoreIdentityScenarioData.Ids.SecondaryTenant;

        RequireSucceeded(await tenantAStore.CreateAsync(userA, CancellationToken.None));
        RequireSucceeded(await tenantBStore.CreateAsync(userB, CancellationToken.None));
        Assert.Equal(userA.Id, (await tenantAStore.FindByNameAsync(userA.NormalizedUserName!, CancellationToken.None))?.Id);
        Assert.Equal(userB.Id, (await tenantBStore.FindByNameAsync(userB.NormalizedUserName!, CancellationToken.None))?.Id);
        Assert.Null(await tenantBStore.FindByIdAsync(userA.Id, CancellationToken.None));
        Assert.Null(await tenantAStore.FindByIdAsync(userB.Id, CancellationToken.None));

        using (var cancellation = new CancellationTokenSource())
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                tenantAStore.FindByIdAsync(userA.Id, cancellation.Token));
        }

        var current = (await tenantAStore.FindByIdAsync(userA.Id, CancellationToken.None))!;
        var stale = (await tenantAStore.FindByIdAsync(userA.Id, CancellationToken.None))!;
        current.DisplayName = "current-winner";
        RequireSucceeded(await tenantAStore.UpdateAsync(current, CancellationToken.None));
        stale.DisplayName = "stale-loser";
        var staleResult = await tenantAStore.UpdateAsync(stale, CancellationToken.None);
        Assert.False(staleResult.Succeeded);
        Assert.Contains(staleResult.Errors, error => error.Code == "ConcurrencyFailure");
        Assert.Equal("current-winner", (await tenantAStore.FindByIdAsync(userA.Id, CancellationToken.None))?.DisplayName);

        var relationshipClaim = new Claim("native-delete", "cascade");
        var relationshipLogin = new UserLoginInfo("native-delete-provider", "native-delete-subject", "Native delete provider");
        var relationshipRole = new IdentityRole
        {
            Id = "native-delete-role",
            Name = "native-delete-role",
            NormalizedName = "NATIVE-DELETE-ROLE",
            ConcurrencyStamp = "native-delete-role-revision-v1"
        };
        var tenantARoleStore = tenantA.RoleStore();
        RequireSucceeded(await tenantARoleStore.CreateAsync(relationshipRole, CancellationToken.None));
        await tenantAStore.AddClaimsAsync(current, [relationshipClaim], CancellationToken.None);
        await tenantAStore.AddLoginAsync(current, relationshipLogin, CancellationToken.None);
        await tenantAStore.AddToRoleAsync(current, relationshipRole.NormalizedName!, CancellationToken.None);
        await tenantAStore.SetTokenAsync(current, "native-delete-provider", "refresh", "native-delete-token", CancellationToken.None);
        Assert.Equal(current.Id, (await tenantAStore.FindByLoginAsync(
            relationshipLogin.LoginProvider,
            relationshipLogin.ProviderKey,
            CancellationToken.None))?.Id);

        var deleteUser = (await tenantAStore.FindByIdAsync(userA.Id, CancellationToken.None))!;
        RequireSucceeded(await tenantAStore.DeleteAsync(deleteUser, CancellationToken.None));
        Assert.Null(await tenantAStore.FindByIdAsync(userA.Id, CancellationToken.None));
        Assert.Null(await tenantAStore.FindByLoginAsync(
            relationshipLogin.LoginProvider,
            relationshipLogin.ProviderKey,
            CancellationToken.None));
        Assert.DoesNotContain(
            await tenantAStore.GetUsersForClaimAsync(relationshipClaim, CancellationToken.None),
            candidate => candidate.Id == deleteUser.Id);
        Assert.DoesNotContain(
            await tenantAStore.GetUsersInRoleAsync(relationshipRole.NormalizedName!, CancellationToken.None),
            candidate => candidate.Id == deleteUser.Id);
        Assert.Equal(userB.Id, (await tenantBStore.FindByIdAsync(userB.Id, CancellationToken.None))?.Id);
    }

    private static async Task RunIndependentClientConcurrencyAsync(
        Func<IStorageProviderConnection> createConnection)
    {
        using var firstPersistence = new AspNetCoreIdentityTestPersistence(createConnection());
        using var secondPersistence = new AspNetCoreIdentityTestPersistence(createConnection());
        var firstFixture = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            firstPersistence);
        var secondFixture = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            secondPersistence);
        await RunConcurrencyAsync(firstFixture, secondFixture);
    }

    private static async Task RunSharedConnectionConcurrencyAsync(
        IStorageProviderConnection connection)
    {
        using var persistence = new AspNetCoreIdentityTestPersistence(
            new NonDisposingStorageProviderConnection(connection));
        var firstFixture = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence);
        var secondFixture = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence);
        await RunConcurrencyAsync(firstFixture, secondFixture);
    }

    private static async Task RunConcurrencyAsync(
        AspNetCoreIdentityGroundworkStoreFixture firstFixture,
        AspNetCoreIdentityGroundworkStoreFixture secondFixture)
    {
        var winner = CreateUser("native-race-a", "native-race", "native-race@example.test");
        var contender = CreateUser("native-race-b", "native-race", "native-race-b@example.test");
        var createResults = await Task.WhenAll(
            firstFixture.UserStore().CreateAsync(winner, CancellationToken.None),
            secondFixture.UserStore().CreateAsync(contender, CancellationToken.None));
        Assert.Single(createResults, result => result.Succeeded);
        Assert.Single(createResults, result => result.Errors.Any(error => error.Code == "DuplicateUserName"));

        var loginOwnerA = CreateUser("native-login-a", "native-login-a", "native-login-a@example.test");
        var loginOwnerB = CreateUser("native-login-b", "native-login-b", "native-login-b@example.test");
        var loginStoreA = firstFixture.UserStore();
        var loginStoreB = secondFixture.UserStore();
        RequireSucceeded(await loginStoreA.CreateAsync(loginOwnerA, CancellationToken.None));
        RequireSucceeded(await loginStoreB.CreateAsync(loginOwnerB, CancellationToken.None));
        var sharedLogin = new UserLoginInfo("native-provider", "native-subject", "Native provider");
        var loginAttempts = await Task.WhenAll(
            CaptureAsync(() => loginStoreA.AddLoginAsync(loginOwnerA, sharedLogin, CancellationToken.None)),
            CaptureAsync(() => loginStoreB.AddLoginAsync(loginOwnerB, sharedLogin, CancellationToken.None)));
        Assert.Single(loginAttempts, exception => exception is null);
        Assert.Single(loginAttempts, exception => exception is InvalidOperationException);
        Assert.Contains(
            (await loginStoreA.FindByLoginAsync(
                sharedLogin.LoginProvider,
                sharedLogin.ProviderKey,
                CancellationToken.None))?.Id,
            new[] { loginOwnerA.Id, loginOwnerB.Id });
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void RequireSucceeded(IdentityResult result) =>
        Assert.True(
            result.Succeeded,
            string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));

    private static AspNetCoreIdentityUser CreateUser(string id, string userName, string email) => new()
    {
        Id = id,
        TenantId = AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        DisplayName = "Native provider user",
        SecurityStamp = "security-v1",
        ConcurrencyStamp = "revision-v1"
    };
}
