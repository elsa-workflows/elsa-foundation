using Elsa.Persistence.Groundwork.DesignConformance.Target;
using Groundwork.PostgreSql;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests;

/// <summary>The composed Groundwork design target on one per-fixture PostgreSQL database inside the shared container.</summary>
internal sealed class PostgreSqlDesignPersistenceContractFixture(string connectionString)
    : GroundworkDesignPersistenceContractFixture("groundwork-postgresql", "PostgreSQL")
{
    public static async Task<PostgreSqlDesignPersistenceContractFixture> CreateAsync(
        PostgreSqlDesignProviderFixture container,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (!container.IsAvailable)
            throw new InvalidOperationException(container.SkipReason ?? "The PostgreSQL design-conformance container is unavailable.");

        var connectionString = await container.CreateDesignDatabaseAsync(cancellationToken);
        return await OpenAsync(new PostgreSqlDesignPersistenceContractFixture(connectionString), cancellationToken);
    }

    protected override IStorageProviderConnection CreateConnection() =>
        new PostgreSqlProviderFactory().Create(connectionString);

    // The per-fixture database is discarded with the shared container, so no cleanup is needed here.
}
