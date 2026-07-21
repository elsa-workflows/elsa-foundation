using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.PostgreSql.DependencyInjection;
using Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests;

/// <summary>Composes the PostgreSQL reference-host shapes and proves a second (SQLite) leaf is rejected.</summary>
public sealed class PostgreSqlProviderLeafConflictContractSuite : ProviderLeafConflictContractSuite
{
    private const string PostgreSqlConnectionString =
        "Host=localhost;Port=5432;Database=elsa;Username=postgres;Password=postgres";
    private const string SqliteConnectionString = "Data Source=:memory:";

    protected override string PrimaryProviderIdentity => "postgresql";
    protected override string ConflictingProviderIdentity => "sqlite";

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

    protected override void AddConflictingProviderLeaf(IServiceCollection services) =>
        services.AddSqliteGroundworkDocumentStore(SqliteConnectionString);
}
