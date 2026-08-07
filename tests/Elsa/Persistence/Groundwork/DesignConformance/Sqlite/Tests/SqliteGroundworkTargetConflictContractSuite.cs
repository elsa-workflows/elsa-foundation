using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>Composes the SQLite reference-host shapes against a second (SQL Server) store.</summary>
public sealed class SqliteGroundworkTargetConflictContractSuite : GroundworkTargetConflictContractSuite
{
    private const string SqliteConnectionString = "Data Source=:memory:";
    private const string SqlServerConnectionString =
        "Server=localhost,1433;Database=elsa;User Id=sa;Password=Placeholder_1;TrustServerCertificate=True";

    protected override string PrimaryProviderIdentity => SqliteGroundworkDocumentStoreRegistration.ProviderIdentity;
    protected override string SecondProviderIdentity => SqlServerGroundworkDocumentStoreRegistration.ProviderIdentity;

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

    protected override void AddSecondStore(IServiceCollection services, string? targetName) =>
        services.AddSqlServerGroundworkDocumentStore(SqlServerConnectionString, targetName: targetName);
}
