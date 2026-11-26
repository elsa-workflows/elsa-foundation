using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// <see cref="ClaimLeaseOverrunContract"/> (#2195) on PostgreSQL, where the two deliverers' writes are not serialized the
/// way SQLite serializes them, so a lost claim surfaces only on each row's concurrency token.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class ClaimLeaseOverrunPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_outbox_dispatches_an_item_whose_claim_lapsed_while_waiting_once_and_the_in_flight_item_may_repeat()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        return ClaimLeaseOverrunContract.OutboxDispatchesAnItemWhoseClaimLapsedWhileWaitingOnceAsync(CreateContext);
    }

    [SkippableFact]
    public Task PostgreSql_timer_pump_fires_a_timer_whose_claim_lapsed_mid_batch_exactly_once()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        return ClaimLeaseOverrunContract.TimerPumpFiresATimerWhoseClaimLapsedMidBatchExactlyOnceAsync(CreateContext);
    }

    private RuntimeDbContext CreateContext() =>
        new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(fixture.ConnectionString).Options);
}
