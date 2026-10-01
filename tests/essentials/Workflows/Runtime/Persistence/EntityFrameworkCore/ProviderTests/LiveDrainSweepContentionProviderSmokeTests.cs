using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.LiveDrainSweepContentionContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// <see cref="LiveDrainSweepContentionContract"/> (#2225) on PostgreSQL, so the claimed-item read and the claim it waits
/// on run against the native provider. Each case starts a whole runtime node, whose module migrator installs the
/// schema, so each gets an empty database of its own.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class LiveDrainSweepContentionPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableTheory]
    [InlineData(LiveDrainSweepTiming.BeforeTheDrainReads)]
    [InlineData(LiveDrainSweepTiming.BetweenTheDrainsReadAndRecord)]
    public async Task PostgreSql_real_sweep_at_the_drains_read_leaves_the_start_with_its_bookmark(LiveDrainSweepTiming timing) =>
        await ARealSweepAtTheDrainsReadLeavesTheStartWithItsBookmarkAsync(await PostgreSqlAsync(), timing);

    [SkippableFact]
    public async Task PostgreSql_drain_waits_for_another_deliverer_that_holds_its_continuation() =>
        await ADrainWaitsForAnotherDelivererThatHoldsItsContinuationAsync(await PostgreSqlAsync());

    [SkippableFact]
    public async Task PostgreSql_drain_delivers_a_continuation_whose_other_claim_lapsed() =>
        await ADrainDeliversAContinuationWhoseOtherClaimLapsedAsync(await PostgreSqlAsync());

    [SkippableFact]
    public async Task PostgreSql_drain_whose_continuation_stays_taken_does_not_report_quiescence() =>
        await ADrainWhoseContinuationStaysTakenDoesNotReportQuiescenceAsync(await PostgreSqlAsync());

    private async Task<Action<IServiceCollection>> PostgreSqlAsync()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        return EntityFramework("PostgreSql", await fixture.CreateEmptyDatabaseAsync("elsa_runtime_live_drain"));
    }
}
