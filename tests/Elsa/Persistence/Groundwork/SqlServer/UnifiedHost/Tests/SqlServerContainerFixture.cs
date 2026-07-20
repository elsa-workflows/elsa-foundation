using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Elsa.Persistence.Groundwork.SqlServer.UnifiedHost.Tests;

/// <summary>Runs the unified-host contract against isolated databases on one SQL Server container.</summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private const string Image = "mcr.microsoft.com/mssql/server:2022-CU21-ubuntu-22.04";
    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Creates a clean database per test so schema admission and lane writes never overlap.</summary>
    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"elsa_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = "master",
            Pooling = false
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}];";
        await command.ExecuteNonQueryAsync();

        return new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = databaseName }.ConnectionString;
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
public sealed class SqlServerContainerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "sqlserver-container";
}
