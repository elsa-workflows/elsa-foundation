using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// Runs <see cref="ProjectionSwitchContract"/> on PostgreSQL, including the scenarios in which another writer commits
/// inside a projection switch's read committed transaction (#2265). Each scenario gets its own persistence scope.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeProjectionSwitchPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    public static TheoryData<string, string> Scenarios => ProjectionSwitchContract.Scenarios;

    public static TheoryData<string, string> InterleavedSwitchScenarios => ProjectionSwitchContract.InterleavedSwitchScenarios;

    [SkippableTheory]
    [MemberData(nameof(Scenarios))]
    public Task PostgreSql(string store, string scenario) => RunAsync(store, scenario);

    [SkippableTheory]
    [MemberData(nameof(InterleavedSwitchScenarios))]
    public Task PostgreSql_interleaved_switches(string store, string scenario) => RunAsync(store, scenario);

    private Task RunAsync(string store, string scenario)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        return ProjectionSwitchContract.RunAsync(store, scenario, interceptors => new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(fixture.ConnectionString).AddInterceptors(interceptors).Options),
            $"projection-switch-{Guid.NewGuid():N}");
    }
}
