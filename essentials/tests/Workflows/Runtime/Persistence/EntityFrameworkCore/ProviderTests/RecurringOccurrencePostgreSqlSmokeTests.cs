using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// <see cref="RecurringOccurrenceDeliveryContract"/> and <see cref="RecurringOccurrenceClaimContract"/> (#2198) on
/// PostgreSQL, where two pumps' claim writes are not serialized the way SQLite serializes them, so a lost race surfaces
/// only on the row's concurrency token. Each case gets an empty database of its own: the runtime node's module migrator
/// installs the schema, migrations included, and the store-only cases create it.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RecurringOccurrencePostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task PostgreSql_an_occurrence_whose_claimant_died_before_routing_starts_once_through_a_peer_after_the_lease()
    {
        var connectionString = await CreateDatabaseAsync();
        await RecurringOccurrenceDeliveryContract.AClaimantThatDiesBeforeRoutingLeavesTheOccurrenceToAPeerAsync("PostgreSql", connectionString, Contexts(connectionString));
    }

    [SkippableFact]
    public async Task PostgreSql_an_occurrence_whose_claimant_died_after_routing_is_repeated_by_a_peer_without_a_second_start()
    {
        var connectionString = await CreateDatabaseAsync();
        await RecurringOccurrenceDeliveryContract.AClaimantThatDiesAfterRoutingIsRepeatedByAPeerWithoutASecondStartAsync("PostgreSql", connectionString, Contexts(connectionString));
    }

    [SkippableFact]
    public async Task PostgreSql_two_pumps_racing_on_one_occurrence_route_and_start_it_once()
    {
        var connectionString = await CreateDatabaseAsync();
        await RecurringOccurrenceDeliveryContract.TwoPumpsRacingOnOneOccurrenceRouteAndStartItOnceAsync("PostgreSql", connectionString, Contexts(connectionString));
    }

    [SkippableFact]
    public async Task PostgreSql_a_republish_while_an_occurrence_is_due_keeps_it_for_the_pump() =>
        await RecurringOccurrenceDeliveryContract.ARepublishWhileAnOccurrenceIsDueKeepsItForThePumpAsync(Contexts(await CreateDatabaseAsync()));

    [SkippableFact]
    public async Task PostgreSql_a_replacement_activated_while_the_replaced_publication_holds_the_occurrence_fires_it_once()
    {
        var connectionString = await CreateDatabaseAsync();
        await RecurringOccurrenceDeliveryContract.AReplacementActivatedWhileTheReplacedPublicationHoldsTheOccurrenceFiresItOnceAsync("PostgreSql", connectionString, Contexts(connectionString));
    }

    [SkippableFact]
    public async Task PostgreSql_a_replacement_activated_after_the_replaced_publication_routed_the_occurrence_does_not_start_it_again()
    {
        var connectionString = await CreateDatabaseAsync();
        await RecurringOccurrenceDeliveryContract.AReplacementActivatedAfterTheReplacedPublicationRoutedTheOccurrenceDoesNotStartItAgainAsync("PostgreSql", connectionString, Contexts(connectionString));
    }

    [SkippableFact]
    public async Task PostgreSql_an_exhausted_cron_fires_its_last_occurrence_once_and_is_then_deleted()
    {
        var connectionString = await CreateDatabaseAsync();
        await RecurringOccurrenceDeliveryContract.AnExhaustedCronFiresItsLastOccurrenceOnceAndIsThenDeletedAsync("PostgreSql", connectionString, Contexts(connectionString));
    }

    [SkippableFact]
    public Task PostgreSql_a_claim_holds_the_occurrence_until_its_lease_lapses() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.AClaimHoldsTheOccurrenceUntilItsLeaseLapsesAsync);

    [SkippableFact]
    public Task PostgreSql_a_release_keeps_the_occurrence_and_counts_the_failure() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.AReleaseKeepsTheOccurrenceAndCountsTheFailureAsync);

    [SkippableFact]
    public Task PostgreSql_deleting_and_saving_the_schedule_again_fences_out_its_claim_without_reissuing_the_token() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.DeletingAndSavingTheScheduleAgainFencesOutItsClaimWithoutReissuingTheTokenAsync);

    [SkippableFact]
    public async Task PostgreSql_two_stores_that_read_one_due_row_grant_exactly_one_claim()
    {
        await using var stores = await new EfRecurringScheduleStores(Contexts(await CreateDatabaseAsync())).EnsureCreatedAsync();
        await RecurringOccurrenceClaimContract.TwoStoresThatReadOneDueRowGrantExactlyOneClaimAsync(stores);
    }

    [SkippableFact]
    public Task PostgreSql_activating_a_replacement_takes_over_the_due_occurrence_and_fences_out_the_replaced_claim() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.ActivatingAReplacementTakesOverTheDueOccurrenceAndFencesOutTheReplacedClaimAsync);

    [SkippableFact]
    public Task PostgreSql_activating_a_replacement_keeps_its_own_cursor_when_no_due_occurrence_of_its_trigger_preceded_it() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.ActivatingAReplacementKeepsItsOwnCursorWhenNoDueOccurrenceOfItsTriggerPrecededItAsync);

    [SkippableFact]
    public Task PostgreSql_activating_a_replacement_takes_over_an_occurrence_that_fell_due_between_its_preparation_and_its_activation() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.ActivatingAReplacementTakesOverAnOccurrenceThatFellDueBetweenItsPreparationAndItsActivationAsync);

    [SkippableFact]
    public Task PostgreSql_compensating_an_activation_that_took_over_a_due_occurrence_restores_the_replaced_schedule_and_removes_the_candidate() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.CompensatingAnActivationThatTookOverADueOccurrenceRestoresTheReplacedScheduleAndRemovesTheCandidateAsync);

    [SkippableFact]
    public Task PostgreSql_compensating_restores_a_replaced_schedule_that_settled_while_it_was_active() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.CompensatingRestoresAReplacedScheduleThatSettledWhileItWasActiveAsync);

    private Task WithStoresAsync(Func<Func<IRecurringTriggerScheduleStore>, Task> scenario) =>
        WithStoresAsync((node, _) => scenario(node));

    private async Task WithStoresAsync(Func<Func<IRecurringTriggerScheduleStore>, FakeTimeProvider, Task> scenario)
    {
        await using var stores = await new EfRecurringScheduleStores(Contexts(await CreateDatabaseAsync())).EnsureCreatedAsync();
        await scenario(() => stores.Create(), stores.Clock);
    }

    private static Func<IInterceptor[], RuntimeDbContext> Contexts(string connectionString) =>
        interceptors => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptors)
            .Options);

    private async Task<string> CreateDatabaseAsync()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var database = $"elsa_runtime_recurring_{Guid.NewGuid():N}";
        await using (var admin = new RuntimePostgreSqlDbContext(
                         new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(fixture.ConnectionString).Options))
            await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE {database}");
        var connection = new DbConnectionStringBuilder { ConnectionString = fixture.ConnectionString };
        connection["Database"] = database;
        return connection.ConnectionString;
    }
}
