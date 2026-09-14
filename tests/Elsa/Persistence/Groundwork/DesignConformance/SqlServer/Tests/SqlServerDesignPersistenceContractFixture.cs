using Elsa.Persistence.Groundwork.DesignConformance.Target;
using Groundwork.SqlServer;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests;

/// <summary>The composed Groundwork design target on one per-fixture SQL Server database inside the shared container.</summary>
internal sealed class SqlServerDesignPersistenceContractFixture(string connectionString)
    : GroundworkDesignPersistenceContractFixture("groundwork-sqlserver", "SQL Server")
{
    public static async Task<SqlServerDesignPersistenceContractFixture> CreateAsync(
        SqlServerDesignProviderFixture container,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (!container.IsAvailable)
            throw new InvalidOperationException(container.SkipReason ?? "The SQL Server design-conformance container is unavailable.");

        var connectionString = await container.CreateDesignDatabaseAsync(cancellationToken);
        return await OpenAsync(new SqlServerDesignPersistenceContractFixture(connectionString), cancellationToken);
    }

    protected override IStorageProviderConnection CreateConnection() =>
        new SqlServerProviderFactory().Create(connectionString);

    // The per-fixture database is discarded with the shared container, so no cleanup is needed here.
}
