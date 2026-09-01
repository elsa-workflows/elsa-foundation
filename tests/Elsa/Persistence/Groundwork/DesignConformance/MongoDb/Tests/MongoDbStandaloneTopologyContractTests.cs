using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.MongoDb.Unified.DependencyInjection;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests;

/// <summary>
/// T054 negative proof: the unified MongoDB substrate composed against an unsupported topology (a
/// standalone server that cannot serve multi-document transactions) may apply its idempotent schema,
/// but must refuse an atomic design write before a unit of work is acquired or any design row is staged.
/// </summary>
[Collection(MongoDbStandaloneTopologyCollection.Name)]
public sealed class MongoDbStandaloneTopologyContractTests(MongoDbStandaloneTopologyFixture container)
{
    [SkippableFact]
    public async Task Standalone_topology_refuses_atomic_design_write_before_any_design_row()
    {
        Skip.IfNot(container.IsAvailable, container.SkipReason ?? "Docker unavailable.");

        var connectionString = container.ConnectionString;
        var databaseName = container.CreateDesignDatabaseName();

        await using var services = BuildServices(connectionString, databaseName);
        var initializers = services.GetServices<IShellInitializer>().ToArray();
        Assert.NotEmpty(initializers);

        foreach (var initializer in initializers)
            await initializer.InitializeAsync(CancellationToken.None);

        // Groundwork v2 admits and applies schema independently from write capability. The atomic
        // boundary is BeginUnitOfWork: it checks the observed deployment before it opens a provider
        // transaction or accepts a staged design row.
        var sessions = services.GetRequiredService<IGroundworkStorageSessionSource>();
        var exception = Assert.Throws<InvalidOperationException>(() => sessions.BeginUnitOfWork(
            StorageAccess.Scoped(new StorageScope("tenant-a")),
            BatchWriteOptions.Exact,
            [WorkflowsDesignStorageManifest.DesignOperationDocumentKind]));

        Assert.Contains("transaction-capable MongoDB replica set", exception.Message, StringComparison.Ordinal);
        Assert.Contains("standalone MongoDB", exception.Message, StringComparison.Ordinal);

        // Schema collections are expected after v2 admission; the transaction refusal must still
        // precede domain data. In particular, every physical operation-ledger collection remains
        // empty, including the scope-specific collection admission created before the topology gate.
        using var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        var operationCollections = (await (await database.ListCollectionNamesAsync(
                cancellationToken: CancellationToken.None))
            .ToListAsync(CancellationToken.None))
            .Where(name => name.StartsWith("elsa_design_operations", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(operationCollections);
        foreach (var collectionName in operationCollections)
        {
            var operations = database.GetCollection<BsonDocument>(collectionName);
            Assert.Equal(0, await operations.CountDocumentsAsync(
                FilterDefinition<BsonDocument>.Empty,
                cancellationToken: CancellationToken.None));
        }
    }

    private static ServiceProvider BuildServices(string connectionString, string databaseName)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddSingleton<ISystemClock>(new DesignPersistenceFixtureData.FixedSystemClock(DesignPersistenceFixtureData.Epoch));
        services.AddSingleton<IPayloadSerializer, DesignPersistenceFixtureData.DeterministicPayloadSerializer>();
        services.AddGroundworkMongoDbUnifiedPersistence(connectionString, databaseName);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
