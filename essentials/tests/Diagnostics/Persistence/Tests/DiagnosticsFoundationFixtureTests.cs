using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsFoundationFixtureTests
{
    [Fact]
    public async Task Restart_double_replays_a_committed_batch_without_duplicate_mutation()
    {
        var target = new DiagnosticsFailureTarget { LoseFirstAcknowledgement = true };
        var batch = new DiagnosticsDrainBatch<int>(DiagnosticsDrainBatchId.New(), [1, 2]);
        await Assert.ThrowsAsync<DiagnosticsOperationalException>(() => target.CommitAsync(batch).AsTask());

        var restarted = target.Restart();
        var replay = await restarted.CommitAsync(batch);

        Assert.Equal(new[] { 1, 2 }, replay.Results);
        Assert.Equal(new[] { 1, 2 }, target.State.PersistedItems);
    }
}
