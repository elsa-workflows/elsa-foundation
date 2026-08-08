using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DesignConformance.Targets.Tests;
using Elsa.Persistence.Groundwork.PostgreSql.DependencyInjection;
using Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests;

/// <summary>Composes the PostgreSQL reference-host shapes against a second (SQLite) store.</summary>
public sealed class PostgreSqlGroundworkTargetConflictContractSuite : GroundworkTargetConflictContractSuite
{
    private const string PostgreSqlConnectionString =
        "Host=localhost;Port=5432;Database=elsa;Username=postgres;Password=postgres";
    private const string SqliteConnectionString = "Data Source=:memory:";

    protected override string PrimaryProviderIdentity => PostgreSqlGroundworkDocumentStoreRegistration.ProviderIdentity;
    protected override string SecondProviderIdentity => SqliteGroundworkDocumentStoreRegistration.ProviderIdentity;

    protected override void ComposePrimary(ReferenceHostShape shape, IServiceCollection services)
    {
        switch (shape)
        {
            case ReferenceHostShape.RuntimeOnly:
                services.AddPostgreSqlGroundworkDocumentStore(PostgreSqlConnectionString);
                services.AddGroundworkRuntimeStores();
                break;
            case ReferenceHostShape.DesignOnly:
                services.AddPostgreSqlGroundworkDocumentStore(PostgreSqlConnectionString);
                services.AddGroundworkWorkflowsDesignStores();
                services.AddGroundworkActivitiesDesignStores();
                break;
            case ReferenceHostShape.Combined:
                services.AddGroundworkPostgreSqlUnifiedPersistence(PostgreSqlConnectionString);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    protected override void AddSecondStore(IServiceCollection services, string? targetName) =>
        services.AddSqliteGroundworkDocumentStore(SqliteConnectionString, targetName: targetName);
}
