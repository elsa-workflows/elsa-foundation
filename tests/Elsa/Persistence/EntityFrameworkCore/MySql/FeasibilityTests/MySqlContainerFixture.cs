using DotNet.Testcontainers.Builders;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests;

public sealed class MySqlContainerFixture : IAsyncLifetime
{
    public const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    public const string ConflictingDatabaseCollation = "utf8mb4_0900_ai_ci";

    private MySqlContainer? container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => container?.GetConnectionString()
        ?? throw new InvalidOperationException("The MySQL test container is not available.");

    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"elsa_mysql_{Guid.NewGuid():N}";
        var admin = MySqlTestContext.Parse(ConnectionString);
        admin.Database = string.Empty;

        await using (var connection = new MySqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE {ConflictingDatabaseCollation}";
            await command.ExecuteNonQueryAsync();
        }

        var isolated = MySqlTestContext.Parse(ConnectionString);
        isolated.Database = databaseName;
        return isolated.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Build() pings Docker; keep it out of the constructor so missing Docker skips the collection.
            container = new MySqlBuilder(Image)
                .WithDatabase("elsa")
                .WithUsername("root")
                .WithPassword("root")
                .Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            IsAvailable = false;
            SkipReason = $"Docker/MySQL container unavailable: {exception.Message}";
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
public sealed class MySqlContainerCollection : ICollectionFixture<MySqlContainerFixture>
{
    public const string Name = "ef-mysql-feasibility";
}
