using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Modularity.ProviderTests;

/// <summary>
/// One database server per engine, shared by that engine's suites, which run one after another in its collection; every test takes
/// a database of its own on it. Without Docker the suites skip, unless <see cref="RequireVariable"/> is set, in which case they
/// fail: a green run is then native-provider evidence.
/// </summary>
public abstract class OpenIddictEngineDatabase(string provider, string engine) : IAsyncLifetime
{
    public const string RequireVariable = "ELSA_WORKBENCH_OPENIDDICT_REQUIRE_NATIVE_PROVIDERS";
    private IAsyncDisposable? _container;
    private string? _serverConnectionString;

    /// <summary>The engine's name as Workbench's <c>Provider</c> setting takes it.</summary>
    public string Provider { get; } = provider;

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            (_container, _serverConnectionString) = await StartAsync();
        }
        catch (Exception exception) when (IsDockerUnavailable(exception) && Environment.GetEnvironmentVariable(RequireVariable) is not ("1" or "true"))
        {
            SkipReason = $"Docker/{engine} unavailable: {exception.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>A new, empty database on the server, and the connection string that reaches it.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var server = _serverConnectionString ?? throw new InvalidOperationException(SkipReason);
        var name = $"openiddict_{Guid.NewGuid():N}";
        await CreateDatabaseAsync(server, name);
        return ConnectionStringFor(server, name);
    }

    protected abstract Task<(IAsyncDisposable Container, string ConnectionString)> StartAsync();

    protected abstract Task CreateDatabaseAsync(string serverConnectionString, string name);

    protected abstract string ConnectionStringFor(string serverConnectionString, string name);

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}

public sealed class PostgreSqlOpenIddictDatabase() : OpenIddictEngineDatabase("PostgreSql", "PostgreSQL")
{
    public const string Collection = "workbench-openiddict-postgresql";

    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    }

    protected override async Task CreateDatabaseAsync(string serverConnectionString, string name)
    {
        await using var connection = new NpgsqlConnection(serverConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    protected override string ConnectionStringFor(string serverConnectionString, string name) =>
        new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = name }.ConnectionString;
}

public sealed class SqlServerOpenIddictDatabase() : OpenIddictEngineDatabase("SqlServer", "SQL Server")
{
    public const string Collection = "workbench-openiddict-sqlserver";

    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    }

    protected override async Task CreateDatabaseAsync(string serverConnectionString, string name)
    {
        await using var connection = new SqlConnection(serverConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
    }

    protected override string ConnectionStringFor(string serverConnectionString, string name) =>
        new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = name }.ConnectionString;
}

[CollectionDefinition(PostgreSqlOpenIddictDatabase.Collection)]
public sealed class PostgreSqlOpenIddictCollection : ICollectionFixture<PostgreSqlOpenIddictDatabase>;

[CollectionDefinition(SqlServerOpenIddictDatabase.Collection)]
public sealed class SqlServerOpenIddictCollection : ICollectionFixture<SqlServerOpenIddictDatabase>;
