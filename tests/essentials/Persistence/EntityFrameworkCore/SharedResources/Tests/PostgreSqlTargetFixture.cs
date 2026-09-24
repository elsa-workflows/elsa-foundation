using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests;

/// <summary>Two physical databases in one disposable PostgreSQL container for shared and split layout tests.</summary>
public sealed class PostgreSqlTargetFixture : IAsyncLifetime
{
    public const string CollectionName = "shared-persistence-postgresql";
    public const string SyntheticPassword = "elsa-shared-persistence-canary";
    private static readonly bool RequirePostgreSql =
        Environment.GetEnvironmentVariable("ELSA_SHARED_PERSISTENCE_REQUIRE_POSTGRESQL") is "1" or "true";

    private PostgreSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string PrimaryConnectionString { get; private set; } = null!;
    public string DiagnosticsConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa_fixture")
                .WithUsername("postgres")
                .WithPassword(SyntheticPassword)
                .Build();
            await _container.StartAsync();
        }
        catch (Exception exception) when (!RequirePostgreSql && IsDockerUnavailable(exception))
        {
            SkipReason = $"Docker/PostgreSQL unavailable: {exception.Message}";
            return;
        }

        var adminConnection = _container.GetConnectionString();
        PrimaryConnectionString = await CreateDatabaseAsync(adminConnection, "primary");
        DiagnosticsConnectionString = await CreateDatabaseAsync(adminConnection, "diagnostics");
        IsAvailable = true;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private static async Task<string> CreateDatabaseAsync(string adminConnection, string role)
    {
        var name = $"elsa_{role}_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{name}\"";
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(adminConnection) { Database = name }.ConnectionString;
    }

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}

[CollectionDefinition(PostgreSqlTargetFixture.CollectionName)]
public sealed class PostgreSqlTargetCollection : ICollectionFixture<PostgreSqlTargetFixture>
{
}
