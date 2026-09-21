using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.SqlServer.Tests;

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => container?.GetConnectionString()
        ?? throw new InvalidOperationException("SQL Server container is not available.");

    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"elsa_{Guid.NewGuid():N}";
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        var builder = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = databaseName };
        return builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        try
        {
            container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            IsAvailable = false;
            SkipReason = $"Docker/SQL Server container unavailable: {exception.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (IsAvailable && container is not null)
            await container.DisposeAsync();
    }

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.Ordinal);
}

[CollectionDefinition(Name)]
public sealed class SqlServerContainerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "secrets-ef-sqlserver";
}
