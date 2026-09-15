using DotNet.Testcontainers.Builders;
using Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// Installs every first-party EF module into one fresh database per provider, then validates each module's
/// history. Unavailable Docker skips unless GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX demands the matrix.
/// </summary>
public sealed class NativeModuleMigrationTests
{
    private static readonly bool RequireNative =
        Environment.GetEnvironmentVariable("GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX") is "1" or "true";

    [SkippableFact]
    public Task Every_module_installs_on_postgresql() => RunAsync("PostgreSql", async () =>
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    });

    [SkippableFact]
    public Task Every_module_installs_on_sql_server() => RunAsync("SqlServer", async () =>
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
    });

    [SkippableFact]
    public Task Every_module_installs_on_mysql() => RunAsync("MySql", async () =>
    {
        var container = new MySqlBuilder("mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a")
            .WithDatabase("elsa").WithUsername("root").WithPassword("root").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    });

    private static async Task RunAsync(string provider, Func<Task<(IAsyncDisposable Container, string ConnectionString)>> start)
    {
        (IAsyncDisposable Container, string ConnectionString) database;
        try
        {
            database = await start();
        }
        catch (Exception exception) when (!RequireNative && IsDockerUnavailable(exception))
        {
            Skip.If(true, $"Docker/{provider} unavailable: {exception.Message}");
            return;
        }

        await using (database.Container)
            await ModuleContextCatalog.InstallAllAsync(provider, database.ConnectionString);
    }

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}
