using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private string? connectionString;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => connectionString ?? container?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL container is not available.");

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
        connectionString = Environment.GetEnvironmentVariable("ELSA_SECRETS_EF_POSTGRESQL_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            IsAvailable = true;
            return;
        }

        try
        {
            // Build() pings Docker; keep it out of the constructor so missing Docker skips instead of failing collection setup.
            container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            IsAvailable = false;
            SkipReason = $"Docker/PostgreSQL container unavailable: {exception.Message}";
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
public sealed class PostgresContainerCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "secrets-ef-postgres";
}
