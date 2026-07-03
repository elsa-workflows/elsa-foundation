using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests;

/// <summary>
/// Starts a single throwaway PostgreSQL container for the whole test collection and exposes its connection
/// string. If Docker is unavailable the container fails to start; the fixture then flips
/// <see cref="IsAvailable"/> to <c>false</c> so integration tests can skip gracefully instead of failing.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("elsa")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Creates a fresh, uniquely-named database on the running container and returns a connection string to it,
    /// so each integration test materializes into an isolated schema and cannot collide with its neighbours.
    /// </summary>
    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"elsa_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;
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
public sealed class PostgresContainerCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "postgres-container";
}
