using System.Security.Claims;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityRelationshipContractTests
{
    [Fact]
    public async Task User_claims_logins_roles_and_tokens_round_trip_through_deterministic_relationships()
    {
        var fixture = CreateFixture();
        var userStore = fixture.UserStore();
        var roleStore = fixture.RoleStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        var userClaim = AspNetCoreIdentityScenarioData.Claims.First(value => value.OwnerKind == AspNetCoreIdentityScenarioData.ClaimOwnerKind.User);
        var replacementClaim = new Claim(userClaim.Type, "identity.users.export");
        var login = AspNetCoreIdentityScenarioData.Logins.First(value => value.UserId == user.Id);
        var token = AspNetCoreIdentityScenarioData.Tokens.First(value => value.Id == AspNetCoreIdentityScenarioData.Ids.PrimaryToken);

        Assert.True((await userStore.CreateAsync(user, CancellationToken.None)).Succeeded);
        Assert.True((await roleStore.CreateAsync(role, CancellationToken.None)).Succeeded);

        await userStore.AddClaimsAsync(user, [AspNetCoreIdentityScenarioData.CreateClaim(userClaim)], CancellationToken.None);
        await userStore.AddLoginAsync(user, AspNetCoreIdentityScenarioData.CreateLoginInfo(login), CancellationToken.None);
        await userStore.AddToRoleAsync(user, role.NormalizedName!, CancellationToken.None);
        await userStore.SetTokenAsync(user, token.LoginProvider, token.Name, token.Value.Reveal(), CancellationToken.None);

        Assert.Equal(userClaim.Value, Assert.Single(await userStore.GetClaimsAsync(user, CancellationToken.None)).Value);
        Assert.Equal(user.Id, Assert.Single(await userStore.GetUsersForClaimAsync(AspNetCoreIdentityScenarioData.CreateClaim(userClaim), CancellationToken.None)).Id);
        Assert.Equal(login.ProviderKey, Assert.Single(await userStore.GetLoginsAsync(user, CancellationToken.None)).ProviderKey);
        Assert.Equal(user.Id, (await userStore.FindByLoginAsync(login.LoginProvider, login.ProviderKey, CancellationToken.None))?.Id);
        Assert.Equal(role.Name, Assert.Single(await userStore.GetRolesAsync(user, CancellationToken.None)));
        Assert.True(await userStore.IsInRoleAsync(user, role.NormalizedName!, CancellationToken.None));
        Assert.Equal(user.Id, Assert.Single(await userStore.GetUsersInRoleAsync(role.NormalizedName!, CancellationToken.None)).Id);
        Assert.Equal(token.Value.Reveal(), await userStore.GetTokenAsync(user, token.LoginProvider, token.Name, CancellationToken.None));

        await userStore.ReplaceClaimAsync(user, AspNetCoreIdentityScenarioData.CreateClaim(userClaim), replacementClaim, CancellationToken.None);
        Assert.Equal("identity.users.export", Assert.Single(await userStore.GetClaimsAsync(user, CancellationToken.None)).Value);

        await userStore.RemoveClaimsAsync(user, [replacementClaim], CancellationToken.None);
        await userStore.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey, CancellationToken.None);
        await userStore.RemoveFromRoleAsync(user, role.NormalizedName!, CancellationToken.None);
        await userStore.RemoveTokenAsync(user, token.LoginProvider, token.Name, CancellationToken.None);

        Assert.Empty(await userStore.GetClaimsAsync(user, CancellationToken.None));
        Assert.Empty(await userStore.GetLoginsAsync(user, CancellationToken.None));
        Assert.Empty(await userStore.GetRolesAsync(user, CancellationToken.None));
        Assert.Null(await userStore.GetTokenAsync(user, token.LoginProvider, token.Name, CancellationToken.None));
    }

    [Fact]
    public async Task Authenticator_key_and_recovery_codes_follow_framework_token_conventions()
    {
        var userStore = CreateFixture().UserStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);

        Assert.True((await userStore.CreateAsync(user, CancellationToken.None)).Succeeded);

        await userStore.SetAuthenticatorKeyAsync(user, AspNetCoreIdentityScenarioData.AuthenticatorKeyValue.Reveal(), CancellationToken.None);
        await userStore.ReplaceCodesAsync(user, ["code-one", "code-two"], CancellationToken.None);

        Assert.Equal(AspNetCoreIdentityScenarioData.AuthenticatorKeyValue.Reveal(), await userStore.GetAuthenticatorKeyAsync(user, CancellationToken.None));
        Assert.Equal(2, await userStore.CountCodesAsync(user, CancellationToken.None));
        Assert.True(await userStore.RedeemCodeAsync(user, "code-one", CancellationToken.None));
        Assert.False(await userStore.RedeemCodeAsync(user, "code-one", CancellationToken.None));
        Assert.Equal(1, await userStore.CountCodesAsync(user, CancellationToken.None));
    }

    private static AspNetCoreIdentityGroundworkStoreFixture CreateFixture() =>
        new(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
}
