using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// Exercises recurring occurrence claims against PostgreSQL using the store's concurrency tokens.
/// Each case gets a separate empty database.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RecurringOccurrencePostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
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
