using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// <see cref="LiveDrainSweepContentionContract"/> (#2225) on PostgreSQL, so the claimed-item read and the claim it waits
/// on run against the native provider. Each case starts a whole runtime node, whose module migrator installs the
/// schema, so each gets an empty database of its own.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class LiveDrainSweepContentionPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    public static TheoryData<string> Scenarios => LiveDrainSweepContentionContract.Scenarios;

    [SkippableTheory]
    [MemberData(nameof(Scenarios))]
    public async Task PostgreSql(string scenario)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        await LiveDrainSweepContentionContract.RunAsync(
            scenario,
            LiveDrainSweepContentionContract.EntityFramework(
                "PostgreSql",
                await fixture.CreateEmptyDatabaseAsync("elsa_runtime_live_drain")));
    }
}
