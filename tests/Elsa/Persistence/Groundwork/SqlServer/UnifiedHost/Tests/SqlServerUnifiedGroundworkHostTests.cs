using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.UnifiedHost.Tests;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.SqlServer.UnifiedHost.Tests;

/// <summary>Production-shaped unified-host coverage for one SQL Server target shared by every Elsa lane.</summary>
[Collection(SqlServerContainerCollection.Name)]
public sealed class SqlServerUnifiedGroundworkHostTests(SqlServerContainerFixture fixture)
{
    private async Task<ServiceProvider> BuildHostAsync()
    {
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>();
        services.AddGroundworkSqlServerUnifiedPersistence(connectionString);

        var provider = services.BuildServiceProvider();
        await provider.ApplySqlServerGroundworkSchemaAsync(connectionString);
        await provider.InitializeGroundworkStoreAsync();
        return provider;
    }

    [SkippableFact]
    public Task Host_registers_one_document_store_shared_by_every_lane()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        return UnifiedGroundworkHostContract.AssertRegistersOneDocumentStoreSharedByEveryLaneAsync(
            () => BuildHostAsync());
    }

    [SkippableFact]
    public Task One_database_materializes_and_serves_all_three_lanes()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        return UnifiedGroundworkHostContract.AssertOneDatabaseMaterializesAndServesAllThreeLanesAsync(
            () => BuildHostAsync());
    }

    [SkippableFact]
    public Task Workflows_design_reads_run_off_the_unified_database()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        return UnifiedGroundworkHostContract.AssertWorkflowsDesignReadsRunOffTheUnifiedDatabaseAsync(
            () => BuildHostAsync());
    }

    [SkippableFact]
    public Task Activities_design_reads_run_off_the_same_unified_database()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        return UnifiedGroundworkHostContract.AssertActivitiesDesignReadsRunOffTheUnifiedDatabaseAsync(
            () => BuildHostAsync());
    }

    [SkippableFact]
    public Task Design_writes_and_reads_run_off_the_one_database()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        return UnifiedGroundworkHostContract.AssertDesignWritesAndReadsRunOffTheOneDatabaseAsync(
            () => BuildHostAsync());
    }
}
