using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsFoundationFixtureTests
{
    public static TheoryData<DiagnosticsProviderKind, DiagnosticsProviderCapability> Providers => new()
    {
        { DiagnosticsProviderKind.Sqlite, DiagnosticsProviderCapability.Relational },
        { DiagnosticsProviderKind.SqlServer, DiagnosticsProviderCapability.Relational },
        { DiagnosticsProviderKind.PostgreSql, DiagnosticsProviderCapability.Relational },
        { DiagnosticsProviderKind.MongoDb, DiagnosticsProviderCapability.Document }
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public void Provider_assertions_apply_the_same_readiness_contract(
        DiagnosticsProviderKind provider,
        DiagnosticsProviderCapability storageCapability)
    {
        var capabilities = new HashSet<DiagnosticsProviderCapability>
        {
            storageCapability,
            DiagnosticsProviderCapability.Transactions,
            DiagnosticsProviderCapability.BoundedQueries,
            DiagnosticsProviderCapability.ExecutionPlans
        };
        var lease = new DiagnosticsProviderLease(provider, "provider-connection", "isolated-database", capabilities);

        DiagnosticsProviderAssertions.IsReady(
            lease,
            storageCapability,
            DiagnosticsProviderCapability.Transactions,
            DiagnosticsProviderCapability.BoundedQueries,
            DiagnosticsProviderCapability.ExecutionPlans);
    }

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
