using System.Security.Claims;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityManagerAcceptanceTests
{
    private const string Password = "Correct1!";

    [Fact]
    public async Task Groundwork_is_the_only_framework_identity_persistence_authority()
    {
        await using var fixture = AspNetCoreIdentityGroundworkAcceptanceFixture.Create();
        await using var scope = fixture.CreateScope();

        Assert.IsType<GroundworkIdentityUserStore>(
            scope.ServiceProvider.GetRequiredService<IUserStore<AspNetCoreIdentityUser>>());
        Assert.IsType<GroundworkIdentityRoleStore>(
            scope.ServiceProvider.GetRequiredService<IRoleStore<IdentityRole>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
        Assert.DoesNotContain(
            fixture.ServiceDescriptors,
            descriptor => IsAspNetCoreIdentityEfDbContext(descriptor.ServiceType) ||
                          IsAspNetCoreIdentityEfDbContext(descriptor.ImplementationType));
    }

    [Fact]
    public async Task UserManager_returns_public_role_names_instead_of_normalized_storage_keys()
    {
        await using var fixture = AspNetCoreIdentityGroundworkAcceptanceFixture.Create();
        await using var scope = fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = CreateUser("role-name-user", "role-name-user", "role-name-user@example.test");
        var role = new IdentityRole("Public Role") { Id = "role-name-role", ConcurrencyStamp = "role-name-revision" };

        RequireSucceeded(await roles.CreateAsync(role));
        RequireSucceeded(await users.CreateAsync(user, Password));
        RequireSucceeded(await users.AddToRoleAsync(user, role.Name!));

        Assert.Equal(["Public Role"], await users.GetRolesAsync(user));
    }

    [Fact]
    public async Task RoleManager_removes_a_claim_after_adding_it_to_the_same_role_instance()
    {
        await using var fixture = AspNetCoreIdentityGroundworkAcceptanceFixture.Create();
        await using var scope = fixture.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var role = new IdentityRole("Claim Role") { Id = "claim-role", ConcurrencyStamp = "claim-role-revision" };
        var claim = new Claim("role-manager-claim", "granted");

        RequireSucceeded(await roles.CreateAsync(role));
        RequireSucceeded(await roles.AddClaimAsync(role, claim));

        RequireSucceeded(await roles.RemoveClaimAsync(role, claim));
        Assert.DoesNotContain(await roles.GetClaimsAsync(role), candidate => candidate.Type == claim.Type && candidate.Value == claim.Value);
    }

    [Fact]
    public async Task Framework_managers_execute_every_advertised_store_capability()
    {
        await using var fixture = AspNetCoreIdentityGroundworkAcceptanceFixture.Create();
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
        Assert.Contains(await users.GetClaimsAsync(user), candidate => candidate.Type == userClaim.Type && candidate.Value == userClaim.Value);
        Assert.Contains(await users.GetUsersForClaimAsync(userClaim), candidate => candidate.Id == user.Id);
        RequireSucceeded(await users.ReplaceClaimAsync(user, userClaim, replacementClaim));
        Assert.Contains(await users.GetClaimsAsync(user), candidate => candidate.Type == replacementClaim.Type && candidate.Value == replacementClaim.Value);

        RequireSucceeded(await roles.AddClaimAsync(role, roleClaim));
        Assert.Contains(await roles.GetClaimsAsync(role), candidate => candidate.Type == roleClaim.Type && candidate.Value == roleClaim.Value);

        RequireSucceeded(await users.AddToRoleAsync(user, role.Name!));
        Assert.True(await users.IsInRoleAsync(user, role.Name!));
        Assert.Single(await users.GetRolesAsync(user));
        Assert.Contains(await users.GetUsersInRoleAsync(role.Name!), candidate => candidate.Id == user.Id);

        RequireSucceeded(await users.SetAuthenticationTokenAsync(user, "manager-provider", "refresh", "refresh-v1"));
        Assert.Equal("refresh-v1", await users.GetAuthenticationTokenAsync(user, "manager-provider", "refresh"));
        RequireSucceeded(await users.RemoveAuthenticationTokenAsync(user, "manager-provider", "refresh"));
        Assert.Null(await users.GetAuthenticationTokenAsync(user, "manager-provider", "refresh"));

        RequireSucceeded(await users.ResetAuthenticatorKeyAsync(user));
        Assert.False(string.IsNullOrWhiteSpace(await users.GetAuthenticatorKeyAsync(user)));
        var generatedRecoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 2);
        Assert.NotNull(generatedRecoveryCodes);
        var recoveryCodes = generatedRecoveryCodes.ToArray();
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

    private static void RequireSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));

    private static AspNetCoreIdentityUser CreateUser(string id, string userName, string email) => new()
    {
        Id = id,
        TenantId = AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
        UserName = userName,
        Email = email,
        DisplayName = "Manager Ada",
        SecurityStamp = "security-v1",
        ConcurrencyStamp = "revision-v1"
    };

    private static bool IsAspNetCoreIdentityEfDbContext(Type? type) =>
        type?.FullName == "Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.ApplicationIdentityDbContext";
}
