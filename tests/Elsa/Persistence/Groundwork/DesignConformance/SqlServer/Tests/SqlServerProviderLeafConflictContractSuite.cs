using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests;

/// <summary>Composes the SQL Server reference-host shapes and proves a second (SQLite) leaf is rejected.</summary>
public sealed class SqlServerProviderLeafConflictContractSuite : ProviderLeafConflictContractSuite
{
    private const string SqlServerConnectionString =
        "Server=localhost,1433;Database=elsa;User Id=sa;Password=Placeholder_1;TrustServerCertificate=True";
    private const string SqliteConnectionString = "Data Source=:memory:";

    protected override string PrimaryProviderIdentity => "sqlserver";
    protected override string ConflictingProviderIdentity => "sqlite";

    protected override void ComposePrimary(ReferenceHostShape shape, IServiceCollection services)
    {
        switch (shape)
        {
            case ReferenceHostShape.RuntimeOnly:
                services.AddSqlServerGroundworkDocumentStore(SqlServerConnectionString);
                services.AddGroundworkRuntimeStores();
                break;
            case ReferenceHostShape.DesignOnly:
                services.AddSqlServerGroundworkDocumentStore(SqlServerConnectionString);
                services.AddGroundworkWorkflowsDesignStores();
                services.AddGroundworkActivitiesDesignStores();
                break;
            case ReferenceHostShape.Combined:
                services.AddGroundworkSqlServerUnifiedPersistence(SqlServerConnectionString);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    protected override void AddConflictingProviderLeaf(IServiceCollection services) =>
        services.AddSqliteGroundworkDocumentStore(SqliteConnectionString);
}
