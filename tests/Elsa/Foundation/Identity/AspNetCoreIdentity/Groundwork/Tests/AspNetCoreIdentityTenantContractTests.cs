using System.Security.Claims;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityTenantContractTests
{
    [Fact]
    public async Task Same_normalized_user_name_and_email_can_exist_in_different_tenants_and_resolve_in_scope()
    {
        var persistence = new AspNetCoreIdentityTestPersistence();
        var tenantA = CreateFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant, persistence, requireUniqueEmail: true).UserStore();
        var tenantB = CreateFixture(AspNetCoreIdentityScenarioData.Ids.SecondaryTenant, persistence, requireUniqueEmail: true).UserStore();
        var userA = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var userB = AspNetCoreIdentityScenarioData.CreateIdentityUser(CrossTenantUser());

        Assert.True((await tenantA.CreateAsync(userA, CancellationToken.None)).Succeeded);
        Assert.True((await tenantB.CreateAsync(userB, CancellationToken.None)).Succeeded);

        var tenantAByName = await tenantA.FindByNameAsync(userA.NormalizedUserName!, CancellationToken.None);
        var tenantBByName = await tenantB.FindByNameAsync(userB.NormalizedUserName!, CancellationToken.None);
        var tenantAByEmail = await tenantA.FindByEmailAsync(userA.NormalizedEmail!, CancellationToken.None);
        var tenantBByEmail = await tenantB.FindByEmailAsync(userB.NormalizedEmail!, CancellationToken.None);

        Assert.Equal(userA.Id, tenantAByName?.Id);
        Assert.Equal(userB.Id, tenantBByName?.Id);
        Assert.Equal(userA.Id, tenantAByEmail?.Id);
        Assert.Equal(userB.Id, tenantBByEmail?.Id);
    }

    [Fact]
    public async Task Wrong_scope_loads_do_not_disclose_existing_users_or_roles()
    {
        var persistence = new AspNetCoreIdentityTestPersistence();
        var tenantA = CreateFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant, persistence);
        var tenantB = CreateFixture(AspNetCoreIdentityScenarioData.Ids.SecondaryTenant, persistence);
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);

        Assert.True((await tenantA.UserStore().CreateAsync(user, CancellationToken.None)).Succeeded);
        Assert.True((await tenantA.RoleStore().CreateAsync(role, CancellationToken.None)).Succeeded);

        Assert.Null(await tenantB.UserStore().FindByIdAsync(user.Id, CancellationToken.None));
        Assert.Null(await tenantB.UserStore().FindByNameAsync(user.NormalizedUserName!, CancellationToken.None));
        Assert.Null(await tenantB.UserStore().FindByEmailAsync(user.NormalizedEmail!, CancellationToken.None));
        Assert.Null(await tenantB.RoleStore().FindByIdAsync(role.Id, CancellationToken.None));
        Assert.Null(await tenantB.RoleStore().FindByNameAsync(role.NormalizedName!, CancellationToken.None));
    }

    [Fact]
    public async Task Store_rejects_entity_tenant_that_does_not_match_the_current_scope()
    {
        var fixture = CreateFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
        var wrongTenantUser = AspNetCoreIdentityScenarioData.CreateIdentityUser(CrossTenantUser());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.UserStore().CreateAsync(wrongTenantUser, CancellationToken.None));
    }

    [Fact]
    public async Task Same_owner_ids_in_different_tenants_do_not_cross_relationship_or_reverse_routes()
    {
        var persistence = new AspNetCoreIdentityTestPersistence();
        var tenantAId = AspNetCoreIdentityScenarioData.Ids.PrimaryTenant;
        var tenantBId = AspNetCoreIdentityScenarioData.Ids.SecondaryTenant;
        var tenantA = CreateFixture(tenantAId, persistence);
        var tenantB = CreateFixture(tenantBId, persistence);
        var userA = SharedIdUser(tenantAId, "tenant-a-user");
        var userB = SharedIdUser(tenantBId, "tenant-b-user");
        var roleA = SharedIdRole("tenant-a-role");
        var roleB = SharedIdRole("tenant-b-role");
        var foreignClaim = new Claim("tenant-isolation", "tenant-b-only");
        var foreignRoleClaim = new Claim("role-tenant-isolation", "tenant-b-only");
        var login = new UserLoginInfo("shared-provider", "shared-subject", "Tenant B login");

        Assert.True((await tenantA.UserStore().CreateAsync(userA, CancellationToken.None)).Succeeded);
        Assert.True((await tenantB.UserStore().CreateAsync(userB, CancellationToken.None)).Succeeded);
        Assert.True((await tenantA.RoleStore().CreateAsync(roleA, CancellationToken.None)).Succeeded);
        Assert.True((await tenantB.RoleStore().CreateAsync(roleB, CancellationToken.None)).Succeeded);

        await tenantB.UserStore().AddClaimsAsync(userB, [foreignClaim], CancellationToken.None);
        await tenantB.UserStore().AddLoginAsync(userB, login, CancellationToken.None);
        await tenantB.UserStore().AddToRoleAsync(userB, roleB.NormalizedName!, CancellationToken.None);
        await tenantB.UserStore().SetTokenAsync(userB, "shared-provider", "shared-token", "tenant-b-secret", CancellationToken.None);
        roleB = (await tenantB.RoleStore().FindByIdAsync(roleB.Id, CancellationToken.None))!;
        await tenantB.RoleStore().AddClaimAsync(roleB, foreignRoleClaim, CancellationToken.None);

        Assert.Empty(await tenantA.UserStore().GetClaimsAsync(userA, CancellationToken.None));
        Assert.Empty(await tenantA.UserStore().GetUsersForClaimAsync(foreignClaim, CancellationToken.None));
        Assert.Empty(await tenantA.UserStore().GetLoginsAsync(userA, CancellationToken.None));
        Assert.Null(await tenantA.UserStore().FindByLoginAsync(login.LoginProvider, login.ProviderKey, CancellationToken.None));
        Assert.Empty(await tenantA.UserStore().GetRolesAsync(userA, CancellationToken.None));
        Assert.False(await tenantA.UserStore().IsInRoleAsync(userA, roleA.NormalizedName!, CancellationToken.None));
        Assert.Empty(await tenantA.UserStore().GetUsersInRoleAsync(roleA.NormalizedName!, CancellationToken.None));
        Assert.Null(await tenantA.UserStore().GetTokenAsync(userA, "shared-provider", "shared-token", CancellationToken.None));
        Assert.Empty(await tenantA.RoleStore().GetClaimsAsync(roleA, CancellationToken.None));
        Assert.Empty(await tenantA.ElsaExternalIdentityStore().ListForUserAsync(tenantAId, userA.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Relationship_and_token_methods_reject_a_user_from_another_tenant_scope()
    {
        var persistence = new AspNetCoreIdentityTestPersistence();
        var tenantAId = AspNetCoreIdentityScenarioData.Ids.PrimaryTenant;
        var tenantBId = AspNetCoreIdentityScenarioData.Ids.SecondaryTenant;
        var tenantA = CreateFixture(tenantAId, persistence);
        var tenantB = CreateFixture(tenantBId, persistence);
        var foreign = SharedIdUser(tenantAId, "foreign-user");
        var tenantARole = SharedIdRole("tenant-a-role");
        var tenantBRole = SharedIdRole("tenant-b-role");
        Assert.True((await tenantA.UserStore().CreateAsync(foreign, CancellationToken.None)).Succeeded);
        Assert.True((await tenantA.RoleStore().CreateAsync(tenantARole, CancellationToken.None)).Succeeded);
        Assert.True((await tenantB.RoleStore().CreateAsync(tenantBRole, CancellationToken.None)).Succeeded);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantB.UserStore().GetClaimsAsync(foreign, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantB.UserStore().AddClaimsAsync(foreign, [new Claim("foreign", "claim")], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantB.UserStore().GetLoginsAsync(foreign, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantB.UserStore().AddLoginAsync(
                foreign,
                new UserLoginInfo("foreign", "subject", "Foreign"),
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantB.UserStore().GetRolesAsync(foreign, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantB.UserStore().AddToRoleAsync(foreign, tenantBRole.NormalizedName!, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantB.UserStore().GetTokenAsync(foreign, "foreign", "token", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantB.UserStore().SetTokenAsync(foreign, "foreign", "token", "secret", CancellationToken.None));
    }

    private static AspNetCoreIdentityGroundworkStoreFixture CreateFixture(
        string tenantId,
        AspNetCoreIdentityTestPersistence? persistence = null,
        bool requireUniqueEmail = false) =>
        new(tenantId, persistence, requireUniqueEmail);

    private static AspNetCoreIdentityScenarioData.User CrossTenantUser() =>
        AspNetCoreIdentityScenarioData.Users.Single(user => user.Id == AspNetCoreIdentityScenarioData.Ids.CrossTenantUser);

    private static AspNetCoreIdentityUser SharedIdUser(string tenantId, string name)
    {
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        user.Id = "shared-user-id";
        user.TenantId = tenantId;
        user.UserName = name;
        user.NormalizedUserName = name.ToUpperInvariant();
        user.Email = $"{name}@example.test";
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        user.ConcurrencyStamp = null;
        return user;
    }

    private static IdentityRole SharedIdRole(string name) => new()
    {
        Id = "shared-role-id",
        Name = name,
        NormalizedName = name.ToUpperInvariant()
    };

}
