using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="RecurringOccurrenceDeliveryContract"/> and <see cref="RecurringOccurrenceClaimContract"/> (#2198) on SQLite, and
/// the claim contract on the in-memory store. PostgreSQL runs them in the provider-test lane.
/// </summary>
public sealed class RecurringOccurrenceTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase _database = new("recurring-occurrences");

    private string ConnectionString => $"{_database.ConnectionString};Pooling=False";

    [Fact]
    public Task Sqlite_an_occurrence_whose_claimant_died_before_routing_starts_once_through_a_peer_after_the_lease() =>
        RecurringOccurrenceDeliveryContract.AClaimantThatDiesBeforeRoutingLeavesTheOccurrenceToAPeerAsync("Sqlite", ConnectionString, CreateContext);

    [Fact]
    public Task Sqlite_an_occurrence_whose_claimant_died_after_routing_is_repeated_by_a_peer_without_a_second_start() =>
        RecurringOccurrenceDeliveryContract.AClaimantThatDiesAfterRoutingIsRepeatedByAPeerWithoutASecondStartAsync("Sqlite", ConnectionString, CreateContext);

    [Fact]
    public Task Sqlite_two_pumps_racing_on_one_occurrence_route_and_start_it_once() =>
        RecurringOccurrenceDeliveryContract.TwoPumpsRacingOnOneOccurrenceRouteAndStartItOnceAsync("Sqlite", ConnectionString, CreateContext);

    [Fact]
    public Task Sqlite_a_republish_while_an_occurrence_is_due_keeps_it_for_the_pump() =>
        RecurringOccurrenceDeliveryContract.ARepublishWhileAnOccurrenceIsDueKeepsItForThePumpAsync(CreateContext);

    [Fact]
    public Task Sqlite_a_claim_holds_the_occurrence_until_its_lease_lapses() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.AClaimHoldsTheOccurrenceUntilItsLeaseLapsesAsync);

    [Fact]
    public Task Sqlite_a_release_keeps_the_occurrence_and_counts_the_failure() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.AReleaseKeepsTheOccurrenceAndCountsTheFailureAsync);

    [Fact]
    public Task Sqlite_rewriting_or_deleting_the_schedule_fences_out_its_claim() =>
        WithStoresAsync(RecurringOccurrenceClaimContract.RewritingOrDeletingTheScheduleFencesOutItsClaimAsync);

    [Fact]
    public Task InMemory_a_claim_holds_the_occurrence_until_its_lease_lapses() =>
        RecurringOccurrenceClaimContract.AClaimHoldsTheOccurrenceUntilItsLeaseLapsesAsync(InMemory());

    [Fact]
    public Task InMemory_a_release_keeps_the_occurrence_and_counts_the_failure() =>
        RecurringOccurrenceClaimContract.AReleaseKeepsTheOccurrenceAndCountsTheFailureAsync(InMemory());

    [Fact]
    public Task InMemory_rewriting_or_deleting_the_schedule_fences_out_its_claim() =>
        RecurringOccurrenceClaimContract.RewritingOrDeletingTheScheduleFencesOutItsClaimAsync(InMemory());

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private async Task WithStoresAsync(Func<Func<IRecurringTriggerScheduleStore>, Task> scenario)
    {
        await using var stores = await new EfRecurringScheduleStores(CreateContext).EnsureCreatedAsync();
        await scenario(() => stores.Create());
    }

    // One store shared by every "node": the in-memory store is the storage.
    private static Func<IRecurringTriggerScheduleStore> InMemory()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        return () => store;
    }

    private RuntimeDbContext CreateContext(IInterceptor[] interceptors) =>
        new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite(ConnectionString)
            .AddInterceptors(interceptors)
            .Options);
}
