using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.LiveDrainSweepContentionContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="LiveDrainSweepContentionContract"/> on the EF Runtime stores over a SQLite file. PostgreSQL runs it in the
/// provider-test lane.
/// </summary>
public sealed class RuntimeEntityFrameworkCoreLiveDrainSweepContentionTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase _database = new("runtime-ef-contention");

    private Action<IServiceCollection> Sqlite => EntityFramework("Sqlite", $"{_database.ConnectionString};Pooling=False");

    [Theory]
    [InlineData(LiveDrainSweepTiming.BeforeTheDrainReads)]
    [InlineData(LiveDrainSweepTiming.BetweenTheDrainsReadAndRecord)]
    public Task A_real_sweep_at_the_drains_read_leaves_the_start_with_its_bookmark(LiveDrainSweepTiming timing) =>
        ARealSweepAtTheDrainsReadLeavesTheStartWithItsBookmarkAsync(Sqlite, timing);

    [Fact]
    public Task A_drain_waits_for_another_deliverer_that_holds_its_continuation() =>
        ADrainWaitsForAnotherDelivererThatHoldsItsContinuationAsync(Sqlite);

    [Fact]
    public Task A_drain_delivers_a_continuation_whose_other_claim_lapsed() =>
        ADrainDeliversAContinuationWhoseOtherClaimLapsedAsync(Sqlite);

    [Fact]
    public Task A_drain_whose_continuation_stays_taken_does_not_report_quiescence() =>
        ADrainWhoseContinuationStaysTakenDoesNotReportQuiescenceAsync(Sqlite);

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
