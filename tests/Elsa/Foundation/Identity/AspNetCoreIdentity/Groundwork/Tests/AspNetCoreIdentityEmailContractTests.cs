using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;
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
        Assert.Empty(fixture.Documents.Snapshot(IdentityStorageManifest.EmailReservationDocumentKind));
    }

    [Fact]
    public async Task Unique_policy_linearizes_one_hundred_independent_same_email_creates()
    {
        var inner = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var fixture = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            inner,
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
        Assert.Single(inner.Snapshot(IdentityStorageManifest.IdentityUserDocumentKind));
        Assert.Single(inner.Snapshot(IdentityStorageManifest.EmailReservationDocumentKind));
    }

    [Fact]
    public async Task Ambiguous_normalized_email_lookup_returns_null()
    {
        var fixture = CreateFixture();
        await SaveUserDocumentAsync(fixture.Documents, User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserOne));
        await SaveUserDocumentAsync(fixture.Documents, User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserTwo));

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
        Assert.Null(await fixture.Documents.LoadAsync(
            IdentityStorageManifest.EmailReservationDocumentKind,
            IdentityDocumentId.From(user.TenantId, stale.NormalizedEmail),
            CancellationToken.None));
        Assert.NotNull(await fixture.Documents.LoadAsync(
            IdentityStorageManifest.EmailReservationDocumentKind,
            IdentityDocumentId.From(user.TenantId, user.NormalizedEmail),
            CancellationToken.None));
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

        Assert.Null(await fixture.Documents.LoadAsync(
            IdentityStorageManifest.EmailReservationDocumentKind,
            IdentityDocumentId.From(user.TenantId, user.NormalizedEmail),
            CancellationToken.None));
    }

    [Fact]
    public async Task Unique_to_nonunique_transition_allows_duplicate_update_and_delete_without_taking_the_owners_reservation()
    {
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var unique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            documents,
            requireUniqueEmail: true).UserStore();
        var nonunique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            documents,
            requireUniqueEmail: false).UserStore();
        var owner = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserOne));
        var duplicate = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserTwo));

        Assert.True((await unique.CreateAsync(owner, CancellationToken.None)).Succeeded);
        Assert.True((await nonunique.CreateAsync(duplicate, CancellationToken.None)).Succeeded);
        duplicate.DisplayName = "Updated after uniqueness was disabled";

        Assert.True((await nonunique.UpdateAsync(duplicate, CancellationToken.None)).Succeeded);
        Assert.True((await nonunique.DeleteAsync(duplicate, CancellationToken.None)).Succeeded);

        var reservationId = IdentityDocumentId.From(owner.TenantId, owner.NormalizedEmail);
        var reservation = await documents.LoadAsync(
            IdentityStorageManifest.EmailReservationDocumentKind,
            reservationId,
            CancellationToken.None);
        Assert.NotNull(reservation);
        var stored = JsonSerializer.Deserialize<IdentityEmailReservationDocument>(
            reservation!.ContentJson,
            IdentityGroundworkJson.Options);
        Assert.Equal(owner.Id, stored?.UserId);

        Assert.True((await nonunique.DeleteAsync(owner, CancellationToken.None)).Succeeded);
        Assert.Null(await documents.LoadAsync(
            IdentityStorageManifest.EmailReservationDocumentKind,
            reservationId,
            CancellationToken.None));
    }

    [Fact]
    public async Task Nonunique_same_email_update_preserves_owned_reservation_against_a_later_unique_create()
    {
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var unique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            documents,
            requireUniqueEmail: true).UserStore();
        var nonunique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            documents,
            requireUniqueEmail: false).UserStore();
        var owner = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserOne));
        var competitor = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.AmbiguousEmailUserTwo));
        Assert.True((await unique.CreateAsync(owner, CancellationToken.None)).Succeeded);

        owner.DisplayName = "Updated while email uniqueness is disabled";
        Assert.True((await nonunique.UpdateAsync(owner, CancellationToken.None)).Succeeded);

        var reservationId = IdentityDocumentId.From(owner.TenantId, owner.NormalizedEmail);
        var reservation = await documents.LoadAsync(
            IdentityStorageManifest.EmailReservationDocumentKind,
            reservationId,
            CancellationToken.None);
        Assert.NotNull(reservation);
        var stored = JsonSerializer.Deserialize<IdentityEmailReservationDocument>(
            reservation!.ContentJson,
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
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        var nonunique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            documents,
            requireUniqueEmail: false).UserStore();
        var unique = new AspNetCoreIdentityGroundworkStoreFixture(
            AspNetCoreIdentityScenarioData.Ids.PrimaryTenant,
            documents,
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
        Assert.Single(documents.Snapshot(IdentityStorageManifest.EmailReservationDocumentKind));
    }

    private static AspNetCoreIdentityGroundworkStoreFixture CreateFixture() =>
        new(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);

    private static AspNetCoreIdentityScenarioData.User User(string id) =>
        AspNetCoreIdentityScenarioData.Users.Single(user => user.Id == id);

    private static async Task SaveUserDocumentAsync(IDocumentStore store, AspNetCoreIdentityScenarioData.User source)
    {
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(source);
        var document = new IdentityUserDocument(
            IdentityCompositeDocumentId.Normalize(source.TenantId),
            IdentityCompositeDocumentId.Normalize(source.Id),
            IdentityCompositeDocumentId.Normalize(source.NormalizedUserName),
            IdentityCompositeDocumentId.Normalize(source.NormalizedEmail),
            ScopedLookupKey(source.TenantId, source.NormalizedUserName),
            ScopedLookupKey(source.TenantId, source.NormalizedEmail),
            AspNetCoreIdentityScenarioData.CreateUserRecord(source),
            new IdentityUserFrameworkState(
                user.Id,
                user.TenantId,
                user.UserName,
                user.NormalizedUserName,
                user.Email,
                user.NormalizedEmail,
                user.EmailConfirmed,
                user.PasswordHash,
                user.SecurityStamp,
                user.ConcurrencyStamp,
                user.PhoneNumber,
                user.PhoneNumberConfirmed,
                user.TwoFactorEnabled,
                user.LockoutEnd,
                user.LockoutEnabled,
                user.AccessFailedCount,
                user.DisplayName));
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);
        await store.SaveAsync(
            new SaveDocumentRequest(
                IdentityStorageManifest.IdentityUserDocumentKind,
                IdentityCompositeDocumentId.From(source.TenantId, source.Id),
                IdentityStorageManifest.SchemaVersion,
                content),
            CancellationToken.None);
    }

    private static string? ScopedLookupKey(string tenantId, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : IdentityDocumentId.From(tenantId, value);

}
