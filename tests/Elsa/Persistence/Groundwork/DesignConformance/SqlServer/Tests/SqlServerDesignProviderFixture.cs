using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests;

/// <summary>
/// Starts a single throwaway SQL Server container for the whole design-conformance test collection and
/// mints one isolated database per fixture. The image is the T001-pinned
/// <c>mcr.microsoft.com/mssql/server:2022-CU21-ubuntu-22.04</c>; T052 records this choice as part of
/// the provider contract work. If Docker is unavailable the container fails to start and
/// <see cref="IsAvailable"/> flips to <c>false</c> with a <see cref="SkipReason"/> so integration
/// callers can degrade instead of throwing opaque connection failures.
/// </summary>
public sealed class SqlServerDesignProviderFixture : IAsyncLifetime
{
    /// <summary>The T001-pinned SQL Server image the design-conformance leaf runs against.</summary>
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU21-ubuntu-22.04";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

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

        await using (var connection = new SqlConnection(new SqlConnectionStringBuilder(AdminConnectionString)
                     {
                         InitialCatalog = "master",
                         Pooling = false
                     }.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}];";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new SqlConnectionStringBuilder(AdminConnectionString) { InitialCatalog = databaseName }.ConnectionString;
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
            SkipReason = $"Docker/SQL Server container unavailable: {exception.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (IsAvailable)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerDesignProviderCollection : ICollectionFixture<SqlServerDesignProviderFixture>
{
    public const string Name = "sqlserver-design-provider";
}
