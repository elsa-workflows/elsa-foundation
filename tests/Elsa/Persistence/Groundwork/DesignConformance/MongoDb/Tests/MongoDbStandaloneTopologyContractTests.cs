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
using MongoDB.Driver;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests;

/// <summary>
/// T054 negative proof: the unified MongoDB substrate composed against an unsupported topology (a
/// standalone server that cannot serve multi-document transactions) must fail readiness during runtime
/// admission <em>before</em> any design document — or even any collection — is written, and
/// direct session access must reject the same incompatibility.
/// </summary>
[Collection(MongoDbStandaloneTopologyCollection.Name)]
public sealed class MongoDbStandaloneTopologyContractTests(MongoDbStandaloneTopologyFixture container)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Standalone_topology_fails_runtime_readiness_before_any_design_write(bool openSessionDirectly)
    {
        Skip.IfNot(container.IsAvailable, container.SkipReason ?? "Docker unavailable.");

        var connectionString = container.ConnectionString;
        var databaseName = container.CreateDesignDatabaseName();

        await using var services = BuildServices(connectionString, databaseName);
        var initializers = services.GetServices<IShellInitializer>().ToArray();
        Assert.NotEmpty(initializers);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (openSessionDirectly)
            {
                services.GetRequiredService<IGroundworkStorageSessionSource>().Open(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    StorageAccess.Scoped(new StorageScope("standalone-proof")));
                return;
            }
            foreach (var initializer in initializers)
                await initializer.InitializeAsync(CancellationToken.None);
        });

        // Unified registration checks the provider's observed atomic-commit capability before
        // admitting schema or opening sessions. A standalone server cannot provide that capability.
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
