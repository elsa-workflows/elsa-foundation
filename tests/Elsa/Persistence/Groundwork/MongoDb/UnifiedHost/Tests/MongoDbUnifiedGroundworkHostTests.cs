using Elsa.Persistence.Groundwork.MongoDb.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.UnifiedHost.Tests;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.MongoDb.UnifiedHost.Tests;

/// <summary>Production-shaped unified-host coverage for one transaction-capable MongoDB target.</summary>
[Collection(MongoDbContainerCollection.Name)]
public sealed class MongoDbUnifiedGroundworkHostTests(MongoDbReplicaSetFixture fixture)
{
    private async Task<ServiceProvider> BuildHostAsync()
    {
        var connectionString = fixture.ConnectionString;
        var databaseName = fixture.CreateIsolatedDatabaseName();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>();
        services.AddGroundworkMongoDbUnifiedPersistence(connectionString, databaseName);

        var provider = services.BuildServiceProvider();
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
