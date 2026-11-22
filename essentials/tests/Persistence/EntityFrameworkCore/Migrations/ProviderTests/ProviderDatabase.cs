using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// One fresh database per provider for the tests that install every first-party EF module into it.
/// Unavailable Docker skips unless ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX demands the matrix.
/// </summary>
internal static class ProviderDatabase
{
    private static readonly bool RequireNative =
        Environment.GetEnvironmentVariable("ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX") is "1" or "true";

    /// <summary>Starts <paramref name="provider"/>, hands the connection string to <paramref name="run"/>, and skips when Docker is not there.</summary>
    public static async Task RunAsync(string provider, Func<string, Task> run)
    {
        (IAsyncDisposable Container, string ConnectionString) database;
        try
        {
            database = await StartAsync(provider);
        }
        catch (Exception exception) when (!RequireNative && IsDockerUnavailable(exception))
        {
            Skip.If(true, $"Docker/{provider} unavailable: {exception.Message}");
            return;
        }

        await using (database.Container)
            await run(database.ConnectionString);
    }

    private static Task<(IAsyncDisposable Container, string ConnectionString)> StartAsync(string provider) => provider switch
    {
        "PostgreSql" => StartPostgreSqlAsync(),
        "SqlServer" => StartSqlServerAsync(),
        "MySql" => StartMySqlAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "No container for this provider.")
    };

    private static async Task<(IAsyncDisposable Container, string ConnectionString)> StartPostgreSqlAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    }

    private static async Task<(IAsyncDisposable Container, string ConnectionString)> StartSqlServerAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();
        var database = new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = "elsa_migrations" }.ConnectionString;
        await using var admin = new SqlConnection(container.GetConnectionString());
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = "CREATE DATABASE [elsa_migrations]";
        await command.ExecuteNonQueryAsync();
        return (container, database);
    }

    private static async Task<(IAsyncDisposable Container, string ConnectionString)> StartMySqlAsync()
    {
        var container = new MySqlBuilder("mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a")
            .WithDatabase("elsa").WithUsername("root").WithPassword("root").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    }

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}
