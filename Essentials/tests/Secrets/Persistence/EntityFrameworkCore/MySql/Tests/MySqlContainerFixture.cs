using DotNet.Testcontainers.Builders;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.MySql.Tests;

public sealed class MySqlContainerFixture : IAsyncLifetime
{
    public const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => container?.GetConnectionString()
        ?? throw new InvalidOperationException("The MySQL test container is not available.");

    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var database = $"elsa_secrets_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(ConnectionString) { Database = string.Empty };
        await using (var connection = new MySqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci";
            await command.ExecuteNonQueryAsync();
        }

        return new MySqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;
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
        catch (Exception exception) when (exception is DockerUnavailableException || exception.InnerException is DockerUnavailableException)
        {
            SkipReason = $"Docker/MySQL container unavailable: {exception.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class MySqlContainerCollection : ICollectionFixture<MySqlContainerFixture>
{
    public const string Name = "secrets-ef-mysql";
}
