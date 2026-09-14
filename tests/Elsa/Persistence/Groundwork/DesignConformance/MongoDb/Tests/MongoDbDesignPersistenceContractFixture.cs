using Elsa.Persistence.Groundwork.DesignConformance.Target;
using Groundwork.MongoDb;
using Groundwork.Store;
using MongoDB.Driver;

namespace Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests;

/// <summary>The composed Groundwork design target on one per-fixture database inside the shared MongoDB replica set.</summary>
internal sealed class MongoDbDesignPersistenceContractFixture(string connectionString, string databaseName)
    : GroundworkDesignPersistenceContractFixture("groundwork-mongodb", "MongoDB")
{
    public static async Task<MongoDbDesignPersistenceContractFixture> CreateAsync(
        MongoDbDesignProviderFixture container,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (!container.IsAvailable)
            throw new InvalidOperationException(container.SkipReason ?? "The MongoDB design-conformance container is unavailable.");

        return await OpenAsync(
            new MongoDbDesignPersistenceContractFixture(container.ConnectionString, container.CreateDesignDatabaseName()),
            cancellationToken);
    }

    protected override IStorageProviderConnection CreateConnection() =>
        new MongoProviderFactory().Create(new MongoUrlBuilder(connectionString) { DatabaseName = databaseName }.ToString());

    // The per-fixture database is discarded with the shared container, so no cleanup is needed here.
}
