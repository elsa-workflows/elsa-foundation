using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Testing;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityRoleStoreContractTests
{
    [Fact]
    public async Task Role_create_find_update_and_delete_round_trip_framework_and_elsa_role_fields()
    {
        var store = CreateStore();
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);

        var create = await store.CreateAsync(role, CancellationToken.None);
        var byId = await store.FindByIdAsync(role.Id, CancellationToken.None);
        var byName = await store.FindByNameAsync(role.NormalizedName!, CancellationToken.None);

        Assert.True(create.Succeeded, ErrorText(create));
        Assert.NotNull(byId);
        Assert.Equal(role.Id, byId!.Id);
        Assert.Equal(role.Name, byId.Name);
        Assert.Equal(role.NormalizedName, byId.NormalizedName);
        Assert.Equal(role.ConcurrencyStamp, byId.ConcurrencyStamp);
        Assert.Equal(role.Id, byName?.Id);

        await store.SetRoleNameAsync(byId, "Operators", CancellationToken.None);
        await store.SetNormalizedRoleNameAsync(byId, "OPERATORS", CancellationToken.None);
        var update = await store.UpdateAsync(byId, CancellationToken.None);
        var updated = await store.FindByNameAsync("OPERATORS", CancellationToken.None);

        Assert.True(update.Succeeded, ErrorText(update));
        Assert.Equal("Operators", updated?.Name);

        var delete = await store.DeleteAsync(updated!, CancellationToken.None);

        Assert.True(delete.Succeeded, ErrorText(delete));
        Assert.Null(await store.FindByIdAsync(role.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Role_claims_are_persisted_listed_and_removed()
    {
        var store = CreateStore();
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        var claim = AspNetCoreIdentityScenarioData.Claims.Single(value => value.OwnerKind == AspNetCoreIdentityScenarioData.ClaimOwnerKind.Role);

        Assert.True((await store.CreateAsync(role, CancellationToken.None)).Succeeded);
        await store.AddClaimAsync(role, AspNetCoreIdentityScenarioData.CreateClaim(claim), CancellationToken.None);

        var claims = await store.GetClaimsAsync(role, CancellationToken.None);

        var storedClaim = Assert.Single(claims);
        Assert.Equal(claim.Type, storedClaim.Type);
        Assert.Equal(claim.Value, storedClaim.Value);

        await store.RemoveClaimAsync(role, storedClaim, CancellationToken.None);

        Assert.Empty(await store.GetClaimsAsync(role, CancellationToken.None));
    }

    [Fact]
    public async Task Role_name_reservation_linearizes_one_hundred_independent_creates()
    {
        var fixture = new AspNetCoreIdentityGroundworkStoreFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
        {
            var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
            role.Id = $"role-race-{index:D3}";
            role.Name = "Shared role";
            role.NormalizedName = "SHARED ROLE";
            return fixture.RoleStore().CreateAsync(role, CancellationToken.None);
        }));

        Assert.Single(results, result => result.Succeeded);
        Assert.Equal(99, results.Count(result => !result.Succeeded));
        Assert.All(
            results.Where(result => !result.Succeeded),
            duplicate => Assert.Contains(duplicate.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateRoleName)));
        Assert.Single(fixture.Documents.Snapshot(IdentityStorageManifest.IdentityRoleDocumentKind));
        Assert.Single(fixture.Documents.Snapshot(IdentityStorageManifest.RoleNameReservationDocumentKind));
    }

    [Fact]
    public async Task Same_role_name_is_allowed_in_different_tenants()
    {
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var firstFixture = new AspNetCoreIdentityGroundworkStoreFixture("tenant-a", documents);
        var secondFixture = new AspNetCoreIdentityGroundworkStoreFixture("tenant-b", documents);
        var first = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        var second = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        first.Id = "role-a";
        second.Id = "role-b";
        first.NormalizedName = second.NormalizedName = "SHARED";

        Assert.True((await firstFixture.RoleStore().CreateAsync(first, CancellationToken.None)).Succeeded);
        Assert.True((await secondFixture.RoleStore().CreateAsync(second, CancellationToken.None)).Succeeded);
        Assert.Equal(2, documents.Snapshot(IdentityStorageManifest.RoleNameReservationDocumentKind).Count);
    }

    private static GroundworkIdentityRoleStore CreateStore() =>
        new AspNetCoreIdentityGroundworkStoreFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant).RoleStore();

    private static string ErrorText(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
}
