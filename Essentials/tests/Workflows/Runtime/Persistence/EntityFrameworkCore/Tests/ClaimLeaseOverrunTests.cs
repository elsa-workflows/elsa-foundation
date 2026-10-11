using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary><see cref="ClaimLeaseOverrunContract"/> on SQLite. The native providers run it in the provider-test lane.</summary>
public sealed class ClaimLeaseOverrunTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase _database = new("lease-overrun");

    [Fact]
    public Task Sqlite_outbox_dispatches_an_item_whose_claim_lapsed_while_waiting_once_and_the_in_flight_item_may_repeat() =>
        ClaimLeaseOverrunContract.OutboxDispatchesAnItemWhoseClaimLapsedWhileWaitingOnceAsync(CreateContext);

    [Fact]
    public Task Sqlite_timer_pump_fires_a_timer_whose_claim_lapsed_mid_batch_exactly_once() =>
        ClaimLeaseOverrunContract.TimerPumpFiresATimerWhoseClaimLapsedMidBatchExactlyOnceAsync(CreateContext);

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private RuntimeDbContext CreateContext() =>
        new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(_database.ConnectionString).Options);
}
