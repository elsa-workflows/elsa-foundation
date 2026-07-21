using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests;

/// <summary>
/// Starts a single throwaway PostgreSQL container for the whole design-conformance test collection and
/// mints one isolated database per fixture. The image is the T001-pinned
/// <c>postgres:17.6-alpine3.22</c>; T053 records this choice as part of the provider contract work.
/// If Docker is unavailable the container fails to start and <see cref="IsAvailable"/> flips to
/// <c>false</c> with a <see cref="SkipReason"/> so integration callers can degrade instead of throwing
/// opaque connection failures.
/// </summary>
public sealed class PostgreSqlDesignProviderFixture : IAsyncLifetime
{
    /// <summary>The T001-pinned PostgreSQL image the design-conformance leaf runs against.</summary>
    public const string Image = "postgres:17.6-alpine3.22";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase("elsa_design")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    /// <summary>The maintenance connection string used only to create per-fixture databases.</summary>
    public string AdminConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Creates a fresh, uniquely-named <c>elsa_design_{guid:N}</c> database on the running container and
    /// returns a connection string to it, so each design fixture materializes into an isolated schema and
    /// cannot collide with its neighbours. Dropping the database on teardown is intentionally optional: the
    /// whole container is discarded when the collection completes.
    /// </summary>
    public async Task<string> CreateDesignDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var databaseName = $"elsa_design_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(AdminConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = databaseName }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (DockerUnavailableException exception)
        {
            IsAvailable = false;
            SkipReason = $"Docker/PostgreSQL container unavailable: {exception.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (IsAvailable)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlDesignProviderCollection : ICollectionFixture<PostgreSqlDesignProviderFixture>
{
    public const string Name = "postgresql-design-provider";
}
