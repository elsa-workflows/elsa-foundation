using System.Data.Common;
using DotNet.Testcontainers.Builders;
using Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.ProviderTests;

/// <summary>
/// One server per engine, shared by that engine's suite, which creates a database of its own for every store, so no test reads
/// a key another wrote. Without Docker the suite skips, unless <see cref="RequireVariable"/> is set, in which case it fails: a
/// green run is then native-provider evidence.
/// </summary>
public abstract class KeyRingServer(string provider, string engine) : IKeyRingDatabase, IAsyncLifetime
{
    public const string RequireVariable = "ELSA_DATA_PROTECTION_EF_REQUIRE_NATIVE_PROVIDERS";
    private IAsyncDisposable? _container;
    private string _administration = "";

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            (_container, _administration) = await StartAsync();
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

    public async Task<KeyRingStore> CreateStoreAsync()
    {
        var database = $"elsa_keys_{Guid.NewGuid():N}";
        await using (var administration = Connect(_administration))
        {
            await administration.OpenAsync();
            await using var command = administration.CreateCommand();
            command.CommandText = CreateDatabase(database);
            await command.ExecuteNonQueryAsync();
        }

        return new KeyRingStore(provider, ConnectionTo(_administration, database));
    }

    /// <summary>Starts the engine's container, returning it and a connection string that may create databases.</summary>
    protected abstract Task<(IAsyncDisposable Container, string Administration)> StartAsync();

    protected abstract DbConnection Connect(string connectionString);

    protected abstract string CreateDatabase(string database);

    protected abstract string ConnectionTo(string administration, string database);

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}

public sealed class PostgreSqlKeyRingServer() : KeyRingServer("PostgreSql", "PostgreSQL")
{
    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    }

    protected override DbConnection Connect(string connectionString) => new NpgsqlConnection(connectionString);

    protected override string CreateDatabase(string database) => $"CREATE DATABASE \"{database}\"";

    protected override string ConnectionTo(string administration, string database) =>
        new NpgsqlConnectionStringBuilder(administration) { Database = database }.ConnectionString;
}

public sealed class SqlServerKeyRingServer() : KeyRingServer("SqlServer", "SQL Server")
{
    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    }

    protected override DbConnection Connect(string connectionString) => new SqlConnection(connectionString);

    protected override string CreateDatabase(string database) => $"CREATE DATABASE [{database}]";

    protected override string ConnectionTo(string administration, string database) =>
        new SqlConnectionStringBuilder(administration) { InitialCatalog = database }.ConnectionString;
}

public sealed class MySqlKeyRingServer() : KeyRingServer("MySql", "MySQL")
{
    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new MySqlBuilder("mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a")
            .WithUsername("root").WithPassword("root").Build();
        await container.StartAsync();
        // No TLS against a throwaway container: MySql.Data negotiates it by default, which nothing here exercises.
        return (container, new MySqlConnectionStringBuilder(container.GetConnectionString()) { SslMode = MySqlSslMode.Disabled }.ConnectionString);
    }

    protected override DbConnection Connect(string connectionString) => new MySqlConnection(connectionString);

    protected override string CreateDatabase(string database) => $"CREATE DATABASE `{database}`";

    protected override string ConnectionTo(string administration, string database) =>
        new MySqlConnectionStringBuilder(administration) { Database = database }.ConnectionString;
}
