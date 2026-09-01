using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Sqlite;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityConcurrencyContractTests
{
    private const int SqliteRaceIterations = 100;

    [Fact]
    public async Task Duplicate_user_normalized_name_in_same_tenant_returns_duplicate_user_name()
    {
        var store = CreateUserStore();
        var original = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var duplicate = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.UnicodeUser));
        duplicate.NormalizedUserName = original.NormalizedUserName;
        duplicate.Email = "unique-duplicate-name@example.test";
        duplicate.NormalizedEmail = "UNIQUE-DUPLICATE-NAME@EXAMPLE.TEST";

        Assert.True((await store.CreateAsync(original, CancellationToken.None)).Succeeded);

        var result = await store.CreateAsync(duplicate, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName));
    }

    [Fact]
    public async Task User_name_reservation_linearizes_one_hundred_independent_creates()
    {
        var fixture = new AspNetCoreIdentityGroundworkStoreFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
        {
            var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
            user.Id = $"user-name-race-{index:D3}";
            user.UserName = $"candidate-{index:D3}";
            user.NormalizedUserName = "SHARED USER NAME";
            user.Email = user.NormalizedEmail = $"candidate-{index:D3}@example.test";
            return fixture.UserStore().CreateAsync(user, CancellationToken.None);
        }));

        Assert.Single(results, result => result.Succeeded);
        Assert.Equal(99, results.Count(result => !result.Succeeded));
        Assert.All(
            results.Where(result => !result.Succeeded),
            duplicate => Assert.Contains(duplicate.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName)));
        Assert.Single(fixture.Snapshot(IdentityStorageManifest.IdentityUserDocumentKind));
        Assert.Single(fixture.Snapshot(IdentityStorageManifest.UserNameReservationDocumentKind));
    }

    [Fact]
    public async Task Duplicate_role_normalized_name_in_same_tenant_returns_duplicate_role_name()
    {
        var store = CreateRoleStore();
        var original = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        var duplicate = AspNetCoreIdentityScenarioData.CreateIdentityRole(Role(AspNetCoreIdentityScenarioData.Ids.UnicodeRole));
        duplicate.NormalizedName = original.NormalizedName;

        Assert.True((await store.CreateAsync(original, CancellationToken.None)).Succeeded);

        var result = await store.CreateAsync(duplicate, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateRoleName));
    }

    [Fact]
    public async Task Duplicate_user_normalized_name_on_update_returns_duplicate_user_name_without_mutating_state()
    {
        var store = CreateUserStore();
        var original = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var second = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.UnicodeUser));
        second.Email = "unique-update-name@example.test";
        second.NormalizedEmail = "UNIQUE-UPDATE-NAME@EXAMPLE.TEST";
        Assert.True((await store.CreateAsync(original, CancellationToken.None)).Succeeded);
        Assert.True((await store.CreateAsync(second, CancellationToken.None)).Succeeded);
        var update = await store.FindByIdAsync(second.Id, CancellationToken.None);
        Assert.NotNull(update);
        update!.NormalizedUserName = original.NormalizedUserName;

        var result = await store.UpdateAsync(update, CancellationToken.None);
        var reloaded = await store.FindByIdAsync(second.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName));
        Assert.Equal(second.NormalizedUserName, reloaded?.NormalizedUserName);
    }

    [Fact]
    public async Task Duplicate_role_normalized_name_on_update_returns_duplicate_role_name_without_mutating_state()
    {
        var store = CreateRoleStore();
        var original = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        var second = AspNetCoreIdentityScenarioData.CreateIdentityRole(Role(AspNetCoreIdentityScenarioData.Ids.UnicodeRole));
        Assert.True((await store.CreateAsync(original, CancellationToken.None)).Succeeded);
        Assert.True((await store.CreateAsync(second, CancellationToken.None)).Succeeded);
        var update = await store.FindByIdAsync(second.Id, CancellationToken.None);
        Assert.NotNull(update);
        update!.NormalizedName = original.NormalizedName;

        var result = await store.UpdateAsync(update, CancellationToken.None);
        var reloaded = await store.FindByIdAsync(second.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateRoleName));
        Assert.Equal(second.NormalizedName, reloaded?.NormalizedName);
    }

    [Fact]
    public async Task Stale_user_update_returns_concurrency_failure_and_preserves_committed_state()
    {
        var store = CreateUserStore();
        var source = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        source.AccessFailedCount = 0;
        Assert.True((await store.CreateAsync(source, CancellationToken.None)).Succeeded);

        var firstCopy = await store.FindByIdAsync(source.Id, CancellationToken.None);
        var staleCopy = await store.FindByIdAsync(source.Id, CancellationToken.None);
        Assert.NotNull(firstCopy);
        Assert.NotNull(staleCopy);

        firstCopy!.DisplayName = "Committed name";
        staleCopy!.DisplayName = "Stale name";

        var first = await store.UpdateAsync(firstCopy, CancellationToken.None);
        var stale = await store.UpdateAsync(staleCopy, CancellationToken.None);
        var reloaded = await store.FindByIdAsync(source.Id, CancellationToken.None);

        Assert.True(first.Succeeded, ErrorText(first));
        Assert.False(stale.Succeeded);
        Assert.Contains(stale.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        Assert.Equal("Committed name", reloaded?.DisplayName);
        Assert.NotEqual(source.ConcurrencyStamp, firstCopy.ConcurrencyStamp);
    }

    [Fact]
    public async Task Malformed_user_stamp_returns_concurrency_failure_without_mutating_state()
    {
        var store = CreateUserStore();
        var source = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        Assert.True((await store.CreateAsync(source, CancellationToken.None)).Succeeded);
        var reloaded = await store.FindByIdAsync(source.Id, CancellationToken.None);
        Assert.NotNull(reloaded);

        reloaded!.DisplayName = "Invalid stamp update";
        reloaded.ConcurrencyStamp = "not-a-groundwork-revision";

        var result = await store.UpdateAsync(reloaded, CancellationToken.None);
        var after = await store.FindByIdAsync(source.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        Assert.NotEqual("Invalid stamp update", after?.DisplayName);
    }

    [Fact]
    public async Task User_stamp_replay_from_another_user_returns_concurrency_failure_without_mutating_state()
    {
        var store = CreateUserStore();
        var first = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var second = AspNetCoreIdentityScenarioData.CreateIdentityUser(User(AspNetCoreIdentityScenarioData.Ids.UnicodeUser));
        second.NormalizedUserName = "UNIQUE-SECOND";
        second.NormalizedEmail = "UNIQUE-SECOND@EXAMPLE.TEST";
        Assert.True((await store.CreateAsync(first, CancellationToken.None)).Succeeded);
        Assert.True((await store.CreateAsync(second, CancellationToken.None)).Succeeded);
        var firstReloaded = await store.FindByIdAsync(first.Id, CancellationToken.None);
        var secondReloaded = await store.FindByIdAsync(second.Id, CancellationToken.None);
        Assert.NotNull(firstReloaded);
        Assert.NotNull(secondReloaded);

        secondReloaded!.DisplayName = "Replay update";
        secondReloaded.ConcurrencyStamp = firstReloaded!.ConcurrencyStamp;

        var result = await store.UpdateAsync(secondReloaded, CancellationToken.None);
        var after = await store.FindByIdAsync(second.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        Assert.NotEqual("Replay update", after?.DisplayName);
    }

    [Fact]
    public async Task Role_stamp_replay_from_another_role_returns_concurrency_failure_without_mutating_state()
    {
        var store = CreateRoleStore();
        var first = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        var second = AspNetCoreIdentityScenarioData.CreateIdentityRole(Role(AspNetCoreIdentityScenarioData.Ids.UnicodeRole));
        second.NormalizedName = "UNIQUE-SECOND-ROLE";
        Assert.True((await store.CreateAsync(first, CancellationToken.None)).Succeeded);
        Assert.True((await store.CreateAsync(second, CancellationToken.None)).Succeeded);
        var firstReloaded = await store.FindByIdAsync(first.Id, CancellationToken.None);
        var secondReloaded = await store.FindByIdAsync(second.Id, CancellationToken.None);
        Assert.NotNull(firstReloaded);
        Assert.NotNull(secondReloaded);

        secondReloaded!.Name = "Replay role";
        secondReloaded.ConcurrencyStamp = firstReloaded!.ConcurrencyStamp;

        var result = await store.UpdateAsync(secondReloaded, CancellationToken.None);
        var after = await store.FindByIdAsync(second.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        Assert.NotEqual("Replay role", after?.Name);
    }

    [Fact]
    public async Task Malformed_role_stamp_returns_concurrency_failure_without_mutating_state()
    {
        var store = CreateRoleStore();
        var source = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        Assert.True((await store.CreateAsync(source, CancellationToken.None)).Succeeded);
        var reloaded = await store.FindByIdAsync(source.Id, CancellationToken.None);
        Assert.NotNull(reloaded);

        reloaded!.Name = "Invalid role stamp update";
        reloaded.ConcurrencyStamp = "not-a-groundwork-role-revision";

        var result = await store.UpdateAsync(reloaded, CancellationToken.None);
        var after = await store.FindByIdAsync(source.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        Assert.NotEqual("Invalid role stamp update", after?.Name);
    }

    [Fact]
    public async Task Missing_user_delete_maps_not_found_without_creating_state()
    {
        var store = CreateUserStore();
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        user.ConcurrencyStamp = IdentityRevisionStamp.FromUser(user.TenantId, user.Id, 1).ToString();

        var result = await store.DeleteAsync(user, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "GroundworkIdentityNotFound");
        Assert.Null(await store.FindByIdAsync(user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_role_delete_maps_not_found_without_creating_state()
    {
        var store = CreateRoleStore();
        var role = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        role.ConcurrencyStamp = IdentityRevisionStamp.FromRole(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant, role.Id, 1).ToString();

        var result = await store.DeleteAsync(role, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "GroundworkIdentityNotFound");
        Assert.Null(await store.FindByIdAsync(role.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Independent_lockout_increments_do_not_lose_updates()
    {
        var store = CreateUserStore();
        var source = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        source.AccessFailedCount = 0;
        Assert.True((await store.CreateAsync(source, CancellationToken.None)).Succeeded);

        var firstCopy = await store.FindByIdAsync(source.Id, CancellationToken.None);
        var secondCopy = await store.FindByIdAsync(source.Id, CancellationToken.None);
        Assert.NotNull(firstCopy);
        Assert.NotNull(secondCopy);

        Assert.Equal(1, await store.IncrementAccessFailedCountAsync(firstCopy!, CancellationToken.None));
        Assert.Equal(2, await store.IncrementAccessFailedCountAsync(secondCopy!, CancellationToken.None));
        var reloaded = await store.FindByIdAsync(source.Id, CancellationToken.None);

        Assert.Equal(2, reloaded?.AccessFailedCount);
    }

    [Fact]
    public async Task Lockout_increment_persists_with_bounded_retry_without_caller_update()
    {
        var store = CreateUserStore();
        var source = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        source.AccessFailedCount = 0;
        Assert.True((await store.CreateAsync(source, CancellationToken.None)).Succeeded);
        var firstCopy = await store.FindByIdAsync(source.Id, CancellationToken.None);
        var secondCopy = await store.FindByIdAsync(source.Id, CancellationToken.None);
        Assert.NotNull(firstCopy);
        Assert.NotNull(secondCopy);

        var first = await store.IncrementAccessFailedCountAsync(firstCopy!, CancellationToken.None);
        var second = await store.IncrementAccessFailedCountAsync(secondCopy!, CancellationToken.None);
        var reloaded = await store.FindByIdAsync(source.Id, CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(2, reloaded?.AccessFailedCount);
        Assert.NotEqual(firstCopy!.ConcurrencyStamp, secondCopy!.ConcurrencyStamp);
    }

    [Fact]
    public async Task Lockout_reset_and_end_date_persist_without_caller_update()
    {
        var store = CreateUserStore();
        var source = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        source.AccessFailedCount = 2;
        source.LockoutEnd = null;
        Assert.True((await store.CreateAsync(source, CancellationToken.None)).Succeeded);
        var user = await store.FindByIdAsync(source.Id, CancellationToken.None);
        Assert.NotNull(user);
        var lockoutEnd = DateTimeOffset.Parse("2042-02-03T05:05:06Z");

        await store.SetLockoutEndDateAsync(user!, lockoutEnd, CancellationToken.None);
        await store.ResetAccessFailedCountAsync(user, CancellationToken.None);
        var reloaded = await store.FindByIdAsync(source.Id, CancellationToken.None);

        Assert.Equal(lockoutEnd, reloaded?.LockoutEnd);
        Assert.Equal(0, reloaded?.AccessFailedCount);
    }

    [Fact]
    public async Task One_hundred_concurrent_recovery_code_redemptions_have_exactly_one_winner()
    {
        var fixture = new AspNetCoreIdentityGroundworkStoreFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
        var store = fixture.UserStore();
        var source = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        Assert.True((await store.CreateAsync(source, CancellationToken.None)).Succeeded);
        await store.ReplaceCodesAsync(source, ["single-use"], CancellationToken.None);
        var contenders = await Task.WhenAll(Enumerable.Range(0, 100).Select(async _ =>
            (await store.FindByIdAsync(source.Id, CancellationToken.None))!));

        var results = await Task.WhenAll(contenders.Select(contender => Task.Run(() =>
            store.RedeemCodeAsync(contender, "single-use", CancellationToken.None))));

        Assert.Single(results, static result => result);
        Assert.Equal(0, await store.CountCodesAsync(
            (await store.FindByIdAsync(source.Id, CancellationToken.None))!,
            CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task File_backed_sqlite_independent_clients_linearize_identity_races()
    {
        var path = Path.Combine(Path.GetTempPath(), $"identity-concurrency-{Guid.NewGuid():N}.db");
        try
        {
            using var persistence = new AspNetCoreIdentityTestPersistence(
                new SqliteProviderFactory().Create($"Data Source={path}"));
            var firstStores = CreateSqliteStores(persistence);
            var secondStores = CreateSqliteStores(persistence);

            for (var iteration = 0; iteration < SqliteRaceIterations; iteration++)
            {
                await DuplicateCreateRaceAsync(firstStores.Users, secondStores.Users, iteration);
                await StaleRevisionRaceAsync(firstStores.Users, secondStores.Users, iteration);
                await LockoutRaceAsync(firstStores.Users, secondStores.Users, iteration);
                await LinkDeleteRaceAsync(firstStores, secondStores, persistence, iteration);
                await SeedLikeCreateRaceAsync(firstStores.Users, secondStores.Users, iteration);
            }
        }
        finally
        {
            File.Delete(path);
            File.Delete($"{path}.schema.lock");
        }
    }

    private static GroundworkIdentityUserStore CreateUserStore() =>
        new AspNetCoreIdentityGroundworkStoreFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant).UserStore();

    private static GroundworkIdentityRoleStore CreateRoleStore() =>
        new AspNetCoreIdentityGroundworkStoreFixture(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant).RoleStore();

    private static AspNetCoreIdentityScenarioData.User User(string id) =>
        AspNetCoreIdentityScenarioData.Users.Single(user => user.Id == id);

    private static AspNetCoreIdentityScenarioData.Role Role(string id) =>
        AspNetCoreIdentityScenarioData.Roles.Single(role => role.Id == id);

    private static string ErrorText(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));

    private static async Task DuplicateCreateRaceAsync(
        GroundworkIdentityUserStore firstStore,
        GroundworkIdentityUserStore secondStore,
        int iteration)
    {
        var first = RaceUser($"duplicate-{iteration}-a");
        var second = RaceUser($"duplicate-{iteration}-b") with
        {
            NormalizedUserName = first.NormalizedUserName,
            NormalizedEmail = first.NormalizedEmail,
            Email = first.Email
        };

        var results = await Task.WhenAll(
            Task.Run(() => firstStore.CreateAsync(AspNetCoreIdentityScenarioData.CreateIdentityUser(first), CancellationToken.None)),
            Task.Run(() => secondStore.CreateAsync(AspNetCoreIdentityScenarioData.CreateIdentityUser(second), CancellationToken.None)));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded);
    }

    private static async Task StaleRevisionRaceAsync(
        GroundworkIdentityUserStore firstStore,
        GroundworkIdentityUserStore secondStore,
        int iteration)
    {
        var source = RaceUser($"stale-{iteration}");
        Assert.True((await firstStore.CreateAsync(
            AspNetCoreIdentityScenarioData.CreateIdentityUser(source),
            CancellationToken.None)).Succeeded);

        var firstCopy = await firstStore.FindByIdAsync(source.Id, CancellationToken.None);
        var secondCopy = await secondStore.FindByIdAsync(source.Id, CancellationToken.None);
        Assert.NotNull(firstCopy);
        Assert.NotNull(secondCopy);
        firstCopy!.DisplayName = $"stale-winner-{iteration}-a";
        secondCopy!.DisplayName = $"stale-winner-{iteration}-b";

        var results = await Task.WhenAll(
            Task.Run(() => firstStore.UpdateAsync(firstCopy, CancellationToken.None)),
            Task.Run(() => secondStore.UpdateAsync(secondCopy, CancellationToken.None)));
        var reloaded = await firstStore.FindByIdAsync(source.Id, CancellationToken.None);

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded);
        Assert.True(
            reloaded?.DisplayName is var name &&
            (name == $"stale-winner-{iteration}-a" || name == $"stale-winner-{iteration}-b"),
            $"Unexpected committed display name '{reloaded?.DisplayName}'.");
    }

    private static async Task LockoutRaceAsync(
        GroundworkIdentityUserStore firstStore,
        GroundworkIdentityUserStore secondStore,
        int iteration)
    {
        var source = RaceUser($"lockout-{iteration}") with { AccessFailedCount = 0 };
        Assert.True((await firstStore.CreateAsync(
            AspNetCoreIdentityScenarioData.CreateIdentityUser(source),
            CancellationToken.None)).Succeeded);

        var firstCopy = await firstStore.FindByIdAsync(source.Id, CancellationToken.None);
        var secondCopy = await secondStore.FindByIdAsync(source.Id, CancellationToken.None);
        Assert.NotNull(firstCopy);
        Assert.NotNull(secondCopy);

        var counts = await Task.WhenAll(
            Task.Run(() => firstStore.IncrementAccessFailedCountAsync(firstCopy!, CancellationToken.None)),
            Task.Run(() => secondStore.IncrementAccessFailedCountAsync(secondCopy!, CancellationToken.None)));
        var reloaded = await firstStore.FindByIdAsync(source.Id, CancellationToken.None);

        Assert.Collection(
            counts.Order(),
            count => Assert.Equal(1, count),
            count => Assert.Equal(2, count));
        Assert.Equal(2, reloaded?.AccessFailedCount);
    }

    private static async Task LinkDeleteRaceAsync(
        SqliteStores linkStores,
        SqliteStores deleteStores,
        AspNetCoreIdentityTestPersistence persistence,
        int iteration)
    {
        var source = RaceUser($"link-delete-{iteration}");
        var role = RaceRole($"link-delete-{iteration}");
        Assert.True((await linkStores.Users.CreateAsync(
            AspNetCoreIdentityScenarioData.CreateIdentityUser(source),
            CancellationToken.None)).Succeeded);
        Assert.True((await linkStores.Roles.CreateAsync(
            AspNetCoreIdentityScenarioData.CreateIdentityRole(role),
            CancellationToken.None)).Succeeded);

        var linkUser = await linkStores.Users.FindByIdAsync(source.Id, CancellationToken.None);
        var deleteUser = await deleteStores.Users.FindByIdAsync(source.Id, CancellationToken.None);
        Assert.NotNull(linkUser);
        Assert.NotNull(deleteUser);

        var linkTask = Task.Run(async () =>
        {
            try
            {
                await linkStores.Users.AddToRoleAsync(linkUser!, role.NormalizedName, CancellationToken.None);
            }
            catch (InvalidOperationException exception) when (IsExpectedLinkDeleteRaceLoser(exception))
            {
                // Valid loser when delete commits first.
            }
        });
        var deleteTask = Task.Run(() => deleteStores.Users.DeleteAsync(deleteUser!, CancellationToken.None));
        await Task.WhenAll(linkTask, deleteTask);

        await AssertNoUserRoleOrphanAsync(persistence, source.TenantId, source.Id, role.Id);
    }

    private static bool IsExpectedLinkDeleteRaceLoser(InvalidOperationException exception) =>
        exception.Message is
            "The requested user does not exist in the current persistence scope." or
            "Groundwork Identity user-role add returned NotFound." or
            "Groundwork Identity user-role add returned ConcurrencyConflict.";

    private static async Task SeedLikeCreateRaceAsync(
        GroundworkIdentityUserStore firstStore,
        GroundworkIdentityUserStore secondStore,
        int iteration)
    {
        var source = RaceUser($"seed-{iteration}");

        var results = await Task.WhenAll(
            Task.Run(() => firstStore.CreateAsync(AspNetCoreIdentityScenarioData.CreateIdentityUser(source), CancellationToken.None)),
            Task.Run(() => secondStore.CreateAsync(AspNetCoreIdentityScenarioData.CreateIdentityUser(source), CancellationToken.None)));
        var reloaded = await firstStore.FindByIdAsync(source.Id, CancellationToken.None);

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded);
        Assert.NotNull(reloaded);
        Assert.Equal(source.NormalizedUserName, reloaded!.NormalizedUserName);
    }

    private static async Task AssertNoUserRoleOrphanAsync(
        AspNetCoreIdentityTestPersistence persistence,
        string tenantId,
        string userId,
        string roleId)
    {
        var accessor = new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
        var rows = persistence.Rows(accessor);
        var linkId = IdentityDocumentId.From(tenantId, userId, roleId);
        var user = LoadUserDocumentOrDefault(rows, tenantId, userId);
        var role = LoadRoleDocument(rows, tenantId, roleId);
        var link = rows.Read(IdentityStorageManifest.UserRoleDocumentKind, linkId);

        Assert.NotNull(role);
        if (link is null)
        {
            Assert.DoesNotContain(linkId, user?.RoleLinkIds ?? []);
            Assert.DoesNotContain(linkId, role!.UserLinkIds ?? []);
            return;
        }

        Assert.NotNull(user);
        Assert.Contains(linkId, user!.RoleLinkIds ?? []);
        Assert.Contains(linkId, role!.UserLinkIds ?? []);
    }

    private static IdentityUserDocument? LoadUserDocumentOrDefault(
        GroundworkIdentityRowStore rows,
        string tenantId,
        string userId)
    {
        var envelope = rows.Read(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId));
        return envelope is null
            ? null
            : JsonSerializer.Deserialize<IdentityUserDocument>(envelope.CanonicalJson, IdentityGroundworkJson.Options);
    }

    private static IdentityRoleDocument? LoadRoleDocument(
        GroundworkIdentityRowStore rows,
        string tenantId,
        string roleId)
    {
        var envelope = rows.Read(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, roleId));
        return envelope is null
            ? null
            : JsonSerializer.Deserialize<IdentityRoleDocument>(envelope.CanonicalJson, IdentityGroundworkJson.Options);
    }

    private static SqliteStores CreateSqliteStores(AspNetCoreIdentityTestPersistence persistence)
    {
        var accessor = new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(
            new PersistenceScope(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant)));
        return new SqliteStores(
            new GroundworkIdentityUserStore(persistence.Rows(accessor), accessor),
            new GroundworkIdentityRoleStore(persistence.Rows(accessor), accessor));
    }

    private static AspNetCoreIdentityScenarioData.User RaceUser(string suffix)
    {
        var id = $"user-race-{suffix}";
        var normalized = $"RACE-{suffix.ToUpperInvariant()}";
        return AspNetCoreIdentityScenarioData.Users.Single(user => user.Id == AspNetCoreIdentityScenarioData.Ids.PrimaryUser) with
        {
            Id = id,
            UserName = $"race-{suffix}",
            NormalizedUserName = normalized,
            Email = $"race-{suffix}@example.test",
            NormalizedEmail = $"RACE-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            DisplayName = $"Race {suffix}",
            SecurityStamp = $"security-race-{suffix}",
            ConcurrencyStamp = $"revision-race-{suffix}",
            RoleIds = new HashSet<string>(StringComparer.Ordinal),
            DirectPermissions = new HashSet<string>(StringComparer.Ordinal)
        };
    }

    private static AspNetCoreIdentityScenarioData.Role RaceRole(string suffix)
    {
        var id = $"role-race-{suffix}";
        return AspNetCoreIdentityScenarioData.Roles.Single(role => role.Id == AspNetCoreIdentityScenarioData.Ids.PrimaryRole) with
        {
            Id = id,
            Name = $"race-role-{suffix}",
            NormalizedName = $"RACE-ROLE-{suffix.ToUpperInvariant()}",
            ConcurrencyStamp = $"role-revision-race-{suffix}"
        };
    }

    private sealed record SqliteStores(GroundworkIdentityUserStore Users, GroundworkIdentityRoleStore Roles);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
