using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.UnifiedHost.Tests;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Groundwork.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests;

/// <summary>
/// End-to-end proof of the headline goal against <b>PostgreSQL</b>: one host-selected database backs every Elsa
/// module. The host composes one provider connection and each provider-level lane explicitly into <b>one</b>
/// PostgreSQL database. Nothing here is PostgreSQL- or Groundwork-specific except the provider connection
/// registration. Skips gracefully when Docker is unavailable.
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
        services
            .AddGroundworkStorageProviderConnection(_ => new PostgreSqlProviderFactory().Create(connectionString))
            .AddGroundworkV2RuntimeStores()
            .AddGroundworkDistributedRuntimeStores()
            .AddGroundworkWorkflowsDesignStores()
            .AddGroundworkActivitiesDesignStores()
            .AddGroundworkPublishingStores();

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
