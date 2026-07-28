using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>Composes the SQLite reference-host shapes and proves a second (SQL Server) leaf is rejected.</summary>
public sealed class SqliteProviderLeafConflictContractSuite : ProviderLeafConflictContractSuite
{
    private const string SqliteConnectionString = "Data Source=:memory:";
    private const string SqlServerConnectionString =
        "Server=localhost,1433;Database=elsa;User Id=sa;Password=Placeholder_1;TrustServerCertificate=True";

    protected override string PrimaryProviderIdentity => "sqlite";
    protected override string ConflictingProviderIdentity => "sqlserver";

    protected override void ComposePrimary(ReferenceHostShape shape, IServiceCollection services)
    {
        switch (shape)
        {
            case ReferenceHostShape.RuntimeOnly:
                services.AddSqliteGroundworkDocumentStore(SqliteConnectionString);
                services.AddGroundworkRuntimeStores();
                break;
            case ReferenceHostShape.DesignOnly:
                services.AddSqliteGroundworkDocumentStore(SqliteConnectionString);
                services.AddGroundworkWorkflowsDesignStores();
                services.AddGroundworkActivitiesDesignStores();
                break;
            case ReferenceHostShape.Combined:
                services.AddGroundworkSqliteUnifiedPersistence(SqliteConnectionString);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    protected override void AddConflictingProviderLeaf(IServiceCollection services) =>
        services.AddSqlServerGroundworkDocumentStore(SqlServerConnectionString);
}
