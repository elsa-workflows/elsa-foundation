using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

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
        => await AspNetCoreIdentityNativeProviderScenario.RunFrameworkCapabilitiesAsync();

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
