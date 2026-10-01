using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public Task PostgreSql_a_claim_holds_the_occurrence_until_its_lease_lapses() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.AClaimHoldsTheOccurrenceUntilItsLeaseLapsesAsync);

    [SkippableFact]
    public Task PostgreSql_a_release_keeps_the_occurrence_and_counts_the_failure() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.AReleaseKeepsTheOccurrenceAndCountsTheFailureAsync);

    [SkippableFact]
    public Task PostgreSql_rewriting_or_deleting_the_schedule_fences_out_its_claim() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.RewritingOrDeletingTheScheduleFencesOutItsClaimAsync);

    private async Task WithStoresAsync(Func<Func<IRecurringTriggerScheduleStore>, Task> scenario)
    {
        await using var stores = await new EfRecurringScheduleStores(Contexts(await CreateDatabaseAsync())).EnsureCreatedAsync();
        await scenario(() => stores.Create());
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
