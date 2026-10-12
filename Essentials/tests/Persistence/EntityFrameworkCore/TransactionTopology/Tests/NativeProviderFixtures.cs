using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests;

public sealed class PostgreSqlTopologyFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;

    public const string CollectionName = "ef-transaction-topology-postgresql";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => container?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL container is not available.");

    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"elsa_topology_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        try
        {
            container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (ContainerSupport.IsUnavailable(exception))
        {
            SkipReason = ContainerSupport.Message("PostgreSQL", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class SqlServerTopologyFixture : IAsyncLifetime
{
    private MsSqlContainer? container;

    public const string CollectionName = "ef-transaction-topology-sqlserver";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => container?.GetConnectionString()
        ?? throw new InvalidOperationException("SQL Server container is not available.");

    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"elsa_topology_{Guid.NewGuid():N}";
        await using var admin = new SqlConnection(ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
        return new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = databaseName }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        try
        {
            container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (ContainerSupport.IsUnavailable(exception))
        {
            SkipReason = ContainerSupport.Message("SQL Server", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class MySqlTopologyFixture : IAsyncLifetime
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? container;

    public const string CollectionName = "ef-transaction-topology-mysql";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => container?.GetConnectionString()
        ?? throw new InvalidOperationException("MySQL container is not available.");

    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        // The fixture owns a fresh container, so its configured database is already isolated.
        await Task.CompletedTask;
        return ConnectionString;
    }

    public async Task InitializeAsync()
    {
        try
        {
            container = new MySqlBuilder(Image)
                .WithDatabase("elsa")
                .WithUsername("root")
                .WithPassword("root")
                .Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (ContainerSupport.IsUnavailable(exception))
        {
            SkipReason = ContainerSupport.Message("MySQL", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

internal static class ContainerSupport
{
    public static bool IsUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsUnavailable(exception.InnerException);

    public static string Message(string provider, Exception exception) =>
        $"Docker/{provider} container unavailable: {exception.Message}";
}

[CollectionDefinition(PostgreSqlTopologyFixture.CollectionName)]
public sealed class PostgreSqlTopologyCollection : ICollectionFixture<PostgreSqlTopologyFixture>
{
}

[CollectionDefinition(SqlServerTopologyFixture.CollectionName)]
public sealed class SqlServerTopologyCollection : ICollectionFixture<SqlServerTopologyFixture>
{
}

[CollectionDefinition(MySqlTopologyFixture.CollectionName)]
public sealed class MySqlTopologyCollection : ICollectionFixture<MySqlTopologyFixture>
{
}
