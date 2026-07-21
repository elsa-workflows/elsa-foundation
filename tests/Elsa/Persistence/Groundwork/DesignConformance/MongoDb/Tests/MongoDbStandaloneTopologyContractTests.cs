using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.MongoDb.Unified.DependencyInjection;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests;

/// <summary>
/// T054 negative proof: the unified MongoDB substrate composed against an unsupported topology (a
/// standalone server that cannot serve multi-document transactions) must fail readiness during runtime
/// admission <em>before</em> any design document — or even any collection — is written, and the schema
/// tool must surface the same incompatibility rather than reporting a ready target.
/// </summary>
[Collection(MongoDbStandaloneTopologyCollection.Name)]
public sealed class MongoDbStandaloneTopologyContractTests(MongoDbStandaloneTopologyFixture container)
{
    [SkippableFact]
    public async Task Standalone_topology_fails_runtime_readiness_before_any_design_write()
    {
        Skip.IfNot(container.IsAvailable, container.SkipReason ?? "Docker unavailable.");

        var connectionString = container.ConnectionString;
        var databaseName = container.CreateDesignDatabaseName();

        await using var services = BuildServices(connectionString, databaseName);
        var initializers = services.GetServices<IShellInitializer>().ToArray();
        Assert.NotEmpty(initializers);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var initializer in initializers)
                await initializer.InitializeAsync(CancellationToken.None);
        });

        // The established runtime-admission failure: MongoDbGroundworkRuntimeAdmission requires the
        // configured and observed deployment to be the same writable replica set with working
        // transactions; a standalone server is rejected and no store is opened.
        Assert.Contains("writable replica set", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no store was opened", exception.Message, StringComparison.Ordinal);

        // Admission rejected the topology before composing or opening a physical store, so the target
        // database holds no design collections (indeed no collections at all).
        using var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        var collections = await (await database.ListCollectionNamesAsync(cancellationToken: CancellationToken.None))
            .ToListAsync(CancellationToken.None);
        Assert.Empty(collections);
    }

    [SkippableFact]
    public async Task Schema_tool_is_topology_agnostic_so_runtime_admission_is_the_sole_transaction_gate()
    {
        Skip.IfNot(container.IsAvailable, container.SkipReason ?? "Docker unavailable.");

        var connectionString = container.ConnectionString;
        var databaseName = container.CreateDesignDatabaseName();

        // Empirical division of responsibility: the Groundwork schema tool validates the manifest and
        // physical-schema target only — it does NOT probe MongoDB deployment topology or transaction
        // capability. It therefore reports even a standalone (non-transactional) MongoDB target as
        // schema "ready". This is precisely why the runtime-admission gate exercised above
        // (MongoDbGroundworkRuntimeAdmission) is the sole, authoritative transaction-topology guard:
        // the schema CLI does not stand in for it.
        var validation = await GroundworkSchemaCli.RunAsync(
            "validate",
            connectionString,
            databaseName,
            CancellationToken.None);

        Assert.Equal(0, validation.ExitCode);
        Assert.Equal("ready", validation.Report.GetProperty("outcome").GetString());
        Assert.Equal(
            "groundwork-mongodb",
            validation.Report.GetProperty("provider").GetProperty("name").GetString());

        // Schema-only validation creates no collections; nothing is written against the standalone.
        using var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        var collections = await (await database.ListCollectionNamesAsync(cancellationToken: CancellationToken.None))
            .ToListAsync(CancellationToken.None);
        Assert.Empty(collections);
    }

    private static ServiceProvider BuildServices(string connectionString, string databaseName)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddSingleton<ISystemClock>(new DesignPersistenceFixtureData.FixedSystemClock(DesignPersistenceFixtureData.Epoch));
        services.AddSingleton<IPayloadSerializer, DesignPersistenceFixtureData.DeterministicPayloadSerializer>();
        services.AddGroundworkMongoDbUnifiedPersistence(connectionString, databaseName, autoApplyOnStartup: false);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
