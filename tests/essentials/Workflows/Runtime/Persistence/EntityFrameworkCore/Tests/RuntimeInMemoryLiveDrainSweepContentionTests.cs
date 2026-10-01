using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.LiveDrainSweepContentionContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary><see cref="LiveDrainSweepContentionContract"/> on the runtime's default in-memory stores.</summary>
public sealed class RuntimeInMemoryLiveDrainSweepContentionTests
{
    [Theory]
    [InlineData(LiveDrainSweepTiming.BeforeTheDrainReads)]
    [InlineData(LiveDrainSweepTiming.BetweenTheDrainsReadAndRecord)]
    public Task A_real_sweep_at_the_drains_read_leaves_the_start_with_its_bookmark(LiveDrainSweepTiming timing) =>
        ARealSweepAtTheDrainsReadLeavesTheStartWithItsBookmarkAsync(InMemory, timing);

    [Fact]
    public Task A_drain_waits_for_another_deliverer_that_holds_its_continuation() =>
        ADrainWaitsForAnotherDelivererThatHoldsItsContinuationAsync(InMemory);

    [Fact]
    public Task A_drain_delivers_a_continuation_whose_other_claim_lapsed() =>
        ADrainDeliversAContinuationWhoseOtherClaimLapsedAsync(InMemory);

    [Fact]
    public Task A_drain_whose_continuation_stays_taken_does_not_report_quiescence() =>
        ADrainWhoseContinuationStaysTakenDoesNotReportQuiescenceAsync(InMemory);
}
