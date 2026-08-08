using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DesignConformance.Targets.Tests;
using Elsa.Persistence.Groundwork.MongoDb.DependencyInjection;
using Elsa.Persistence.Groundwork.MongoDb.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests;

/// <summary>Composes the MongoDB reference-host shapes against a second (SQLite) store.</summary>
public sealed class MongoDbGroundworkTargetConflictContractSuite : GroundworkTargetConflictContractSuite
{
    private const string MongoDbConnectionString = "mongodb://localhost:27017/?replicaSet=rs0";
    private const string MongoDbDatabaseName = "elsa";
    private const string SqliteConnectionString = "Data Source=:memory:";

    protected override string PrimaryProviderIdentity => MongoDbGroundworkDocumentStoreRegistration.ProviderIdentity;
    protected override string SecondProviderIdentity => SqliteGroundworkDocumentStoreRegistration.ProviderIdentity;

    protected override void ComposePrimary(ReferenceHostShape shape, IServiceCollection services)
    {
        switch (shape)
        {
            case ReferenceHostShape.RuntimeOnly:
                services.AddMongoDbGroundworkDocumentStore(MongoDbConnectionString, MongoDbDatabaseName);
                services.AddGroundworkRuntimeStores();
                break;
            case ReferenceHostShape.DesignOnly:
                services.AddMongoDbGroundworkDocumentStore(MongoDbConnectionString, MongoDbDatabaseName);
                services.AddGroundworkWorkflowsDesignStores();
                services.AddGroundworkActivitiesDesignStores();
                break;
            case ReferenceHostShape.Combined:
                services.AddGroundworkMongoDbUnifiedPersistence(MongoDbConnectionString, MongoDbDatabaseName);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    protected override void AddSecondStore(IServiceCollection services, string? targetName) =>
        services.AddSqliteGroundworkDocumentStore(SqliteConnectionString, targetName: targetName);
}
