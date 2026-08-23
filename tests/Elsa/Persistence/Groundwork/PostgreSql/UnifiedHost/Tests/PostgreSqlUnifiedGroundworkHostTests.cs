using Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.UnifiedHost.Tests;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests;

/// <summary>
/// End-to-end proof of the headline goal against <b>PostgreSQL</b>: one host-selected database backs every Elsa
/// module. The host composes a single feature (<c>AddGroundworkPostgreSqlUnifiedPersistence</c>) which
/// materializes the six provider-level feature manifests into <b>one</b> PostgreSQL database and points every
/// family's neutral ports at it. Nothing here is PostgreSQL- or Groundwork-specific except the one host
/// registration call. Skips gracefully when Docker is unavailable.
/// </summary>
[Collection(PostgresContainerCollection.Name)]
public sealed class PostgreSqlUnifiedGroundworkHostTests(PostgresContainerFixture fixture)
{
    private async Task<ServiceProvider> BuildHostAsync()
    {
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>();
        services.AddGroundworkPostgreSqlUnifiedPersistence(connectionString);

        var provider = services.BuildServiceProvider();
        // A bare provider has no host lifecycle; drive runtime admission after explicit schema application.
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    [SkippableFact]
    public Task Host_registers_one_provider_connection_shared_by_every_lane()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        return UnifiedGroundworkHostContract.AssertRegistersOneProviderConnectionSharedByEveryLaneAsync(
            () => BuildHostAsync());
    }

    [SkippableFact]
    public Task One_database_materializes_and_serves_every_lane()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        return UnifiedGroundworkHostContract.AssertOneDatabaseMaterializesAndServesEveryLaneAsync(
            () => BuildHostAsync(),
            "default");
    }

    [SkippableFact]
    public Task Activities_design_reads_run_off_the_same_unified_database()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        return UnifiedGroundworkHostContract.AssertActivitiesDesignReadsRunOffTheUnifiedDatabaseAsync(
            () => BuildHostAsync(),
            "default");
    }

    [SkippableFact]
    public Task Design_writes_and_reads_run_off_the_one_database()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        return UnifiedGroundworkHostContract.AssertDesignWritesAndReadsRunOffTheOneDatabaseAsync(
            () => BuildHostAsync());
    }
}
