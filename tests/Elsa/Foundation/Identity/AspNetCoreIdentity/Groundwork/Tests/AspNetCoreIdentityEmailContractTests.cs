using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityEmailContractTests
{
    [Fact]
    public async Task Default_policy_allows_duplicate_email_without_creating_reservations()
    {
        var fixture = CreateFixture();
        var store = fixture.UserStore();
        var first = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserOne));
        var second = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserTwo));

        Assert.True((await store.CreateAsync(first, CancellationToken.None)).Succeeded);

        var result = await store.CreateAsync(second, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(fixture.Snapshot(IdentityStorageManifest.EmailReservationDocumentKind));
    }

    [Fact]
    public async Task Unique_policy_linearizes_one_hundred_independent_same_email_creates()
    {
        var persistence = new AspNetCoreIdentityTestPersistence();
        var fixture = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence,
            requireUniqueEmail: true);
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
        {
            var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(
                User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserOne));
            user.Id = $"race-{index:D3}";
            user.UserName = $"race-{index:D3}";
            user.NormalizedUserName = user.UserName.ToUpperInvariant();
            return fixture.UserStore().CreateAsync(user, CancellationToken.None);
        }));

        Assert.Single(results, result => result.Succeeded);
        Assert.Equal(99, results.Count(result => !result.Succeeded));
        Assert.All(
            results.Where(result => !result.Succeeded),
            duplicate => Assert.Contains(duplicate.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateEmail)));
        Assert.Single(fixture.Snapshot(IdentityStorageManifest.IdentityUserDocumentKind));
        Assert.Single(fixture.Snapshot(IdentityStorageManifest.EmailReservationDocumentKind));
    }

    [Fact]
    public async Task Ambiguous_normalized_email_lookup_returns_null()
    {
        var fixture = CreateFixture();
        var first = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserOne));
        var second = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserTwo));
        Assert.True((await fixture.UserStore().CreateAsync(first, CancellationToken.None)).Succeeded);
        Assert.True((await fixture.UserStore().CreateAsync(second, CancellationToken.None)).Succeeded);

        Assert.Null(await fixture.UserStore().FindByEmailAsync("SHARED@EXAMPLE.TEST", CancellationToken.None));
    }

    [Fact]
    public async Task Stale_user_update_rolls_back_the_new_email_reservation()
    {
        var fixture = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            requireUniqueEmail: true);
        var store = fixture.UserStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        Assert.True((await store.CreateAsync(user, CancellationToken.None)).Succeeded);
        var stale = (await store.FindByIdAsync(user.Id, CancellationToken.None))!;

        user.Email = user.NormalizedEmail = "CURRENT@EXAMPLE.TEST";
        Assert.True((await store.UpdateAsync(user, CancellationToken.None)).Succeeded);
        stale.Email = stale.NormalizedEmail = "STALE@EXAMPLE.TEST";

        var staleResult = await store.UpdateAsync(stale, CancellationToken.None);

        Assert.False(staleResult.Succeeded);
        Assert.Null(fixture.Read(
            IdentityStorageManifest.EmailReservationDocumentKind,
            IdentityDocumentId.From(user.TenantId, stale.NormalizedEmail!)));
        Assert.NotNull(fixture.Read(
            IdentityStorageManifest.EmailReservationDocumentKind,
            IdentityDocumentId.From(user.TenantId, user.NormalizedEmail!)));
    }

    [Fact]
    public async Task Aggregate_delete_removes_unique_email_reservation_with_the_user()
    {
        var fixture = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            requireUniqueEmail: true);
        var store = fixture.UserStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        Assert.True((await store.CreateAsync(user, CancellationToken.None)).Succeeded);

        Assert.True((await store.DeleteAsync(user, CancellationToken.None)).Succeeded);

        Assert.Null(fixture.Read(
            IdentityStorageManifest.EmailReservationDocumentKind,
            IdentityDocumentId.From(user.TenantId, user.NormalizedEmail!)));
    }

    [Fact]
    public async Task Unique_to_nonunique_transition_allows_duplicate_update_and_delete_without_taking_the_owners_reservation()
    {
        var persistence = new AspNetCoreIdentityTestPersistence();
        var unique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence,
            requireUniqueEmail: true).UserStore();
        var nonunique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence,
            requireUniqueEmail: false).UserStore();
        var owner = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserOne));
        var duplicate = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserTwo));

        Assert.True((await unique.CreateAsync(owner, CancellationToken.None)).Succeeded);
        Assert.True((await nonunique.CreateAsync(duplicate, CancellationToken.None)).Succeeded);
        duplicate.DisplayName = "Updated after uniqueness was disabled";

        Assert.True((await nonunique.UpdateAsync(duplicate, CancellationToken.None)).Succeeded);
        Assert.True((await nonunique.DeleteAsync(duplicate, CancellationToken.None)).Succeeded);

        var reservationId = IdentityDocumentId.From(owner.TenantId, owner.NormalizedEmail);
        var reservation = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence).Read(IdentityStorageManifest.EmailReservationDocumentKind, reservationId);
        Assert.NotNull(reservation);
        var stored = JsonSerializer.Deserialize<IdentityEmailReservationDocument>(
            reservation!.CanonicalJson,
            IdentityGroundworkJson.Options);
        Assert.Equal(owner.Id, stored?.UserId);

        Assert.True((await nonunique.DeleteAsync(owner, CancellationToken.None)).Succeeded);
        Assert.Null(new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence).Read(IdentityStorageManifest.EmailReservationDocumentKind, reservationId));
    }

    [Fact]
    public async Task Nonunique_same_email_update_preserves_owned_reservation_against_a_later_unique_create()
    {
        var persistence = new AspNetCoreIdentityTestPersistence();
        var unique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence,
            requireUniqueEmail: true).UserStore();
        var nonunique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence,
            requireUniqueEmail: false).UserStore();
        var owner = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserOne));
        var competitor = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserTwo));
        Assert.True((await unique.CreateAsync(owner, CancellationToken.None)).Succeeded);

        owner.DisplayName = "Updated while email uniqueness is disabled";
        Assert.True((await nonunique.UpdateAsync(owner, CancellationToken.None)).Succeeded);

        var reservationId = IdentityDocumentId.From(owner.TenantId, owner.NormalizedEmail);
        var reservation = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence).Read(IdentityStorageManifest.EmailReservationDocumentKind, reservationId);
        Assert.NotNull(reservation);
        var stored = JsonSerializer.Deserialize<IdentityEmailReservationDocument>(
            reservation!.CanonicalJson,
            IdentityGroundworkJson.Options);
        Assert.Equal(owner.Id, stored?.UserId);

        var conflict = await unique.CreateAsync(competitor, CancellationToken.None);
        Assert.False(conflict.Succeeded);
        Assert.Contains(conflict.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateEmail));
        Assert.Null(await unique.FindByIdAsync(competitor.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Nonunique_to_unique_transition_has_one_owner_and_releases_the_key_for_the_loser()
    {
        var persistence = new AspNetCoreIdentityTestPersistence();
        var nonunique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence,
            requireUniqueEmail: false).UserStore();
        var unique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence,
            requireUniqueEmail: true).UserStore();
        var first = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserOne));
        var second = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserTwo));
        Assert.True((await nonunique.CreateAsync(first, CancellationToken.None)).Succeeded);
        Assert.True((await nonunique.CreateAsync(second, CancellationToken.None)).Succeeded);

        first.DisplayName = "First uniqueness contender";
        second.DisplayName = "Second uniqueness contender";
        Assert.True((await unique.UpdateAsync(first, CancellationToken.None)).Succeeded);

        var conflict = await unique.UpdateAsync(second, CancellationToken.None);
        Assert.False(conflict.Succeeded);
        Assert.Contains(conflict.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateEmail));

        Assert.True((await unique.DeleteAsync(first, CancellationToken.None)).Succeeded);
        Assert.True((await unique.UpdateAsync(second, CancellationToken.None)).Succeeded);
        Assert.Single(new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            persistence).Snapshot(IdentityStorageManifest.EmailReservationDocumentKind));
    }

    private static AspNetCoreIdentityGroundworkStoreFixture CreateFixture() =>
        new(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);

    private static AspNetCoreIdentityScenarioData.User User(string id) =>
        AspNetCoreIdentityScenarioData.Users.Single(user => user.Id == id);

}
