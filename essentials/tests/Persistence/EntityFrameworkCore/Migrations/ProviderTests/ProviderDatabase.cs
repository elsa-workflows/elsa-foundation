using System.Data.Common;
using System.Runtime.ExceptionServices;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// One fresh database per provider for the tests that install every first-party EF module into it.
/// Explicit disposable-server connections take precedence over Testcontainers. Unavailable Docker
/// skips unless ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX demands the matrix.
/// </summary>
internal static class ProviderDatabase
{
    private static readonly bool RequireNative =
        Environment.GetEnvironmentVariable("ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX") is "1" or "true";

    /// <summary>Creates an isolated database, hands its connection to <paramref name="run"/>, and cleans up its owner.</summary>
    public static async Task RunAsync(string provider, Func<string, Task> run)
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable(provider));
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var external = CreateExternalPlan(provider, configured);
            // An explicitly configured provider fails loudly; it never falls back to Docker or skips.
            await RunExternalAsync(external, run, (sql, confirmed) => ExecuteAdminAsync(external, sql, confirmed));
            return;
        }

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

    internal static string ConnectionEnvironmentVariable(string provider) => provider switch
    {
        "PostgreSql" => "ELSA_MIGRATIONS_EF_POSTGRESQL_TEST_CONNECTION_STRING",
        "SqlServer" => "ELSA_MIGRATIONS_EF_SQLSERVER_TEST_CONNECTION_STRING",
        "MySql" => "ELSA_MIGRATIONS_EF_MYSQL_TEST_CONNECTION_STRING",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "No test database for this provider.")
    };

    internal static ExternalDatabasePlan CreateExternalPlan(string provider, string configured)
    {
        var name = "elsa_migrations_" + Guid.NewGuid().ToString("N");
        var identifier = OwnedDatabaseIdentifier(provider, name);
        // Disable pooling only on these run-local connections; never clear a shared provider pool.
        switch (provider)
        {
            case "PostgreSql":
                var postgres = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
                return new(name, new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = name }.ConnectionString,
                    () => new NpgsqlConnection(postgres.ConnectionString), $"CREATE DATABASE {identifier}", $"DROP DATABASE {identifier}");
            case "SqlServer":
                var sqlServer = new SqlConnectionStringBuilder(configured) { Pooling = false };
                return new(name, new SqlConnectionStringBuilder(sqlServer.ConnectionString) { InitialCatalog = name }.ConnectionString,
                    () => new SqlConnection(sqlServer.ConnectionString), $"CREATE DATABASE {identifier}", $"DROP DATABASE {identifier}");
            case "MySql":
                var mySql = new MySqlConnectionStringBuilder(configured) { Pooling = false };
                return new(name, new MySqlConnectionStringBuilder(mySql.ConnectionString) { Database = name }.ConnectionString,
                    () => new MySqlConnection(mySql.ConnectionString), $"CREATE DATABASE {identifier} CHARACTER SET utf8mb4", $"DROP DATABASE {identifier}");
            default:
                throw new ArgumentOutOfRangeException(nameof(provider));
        }
    }

    internal static string OwnedDatabaseIdentifier(string provider, string name)
    {
        const string prefix = "elsa_migrations_";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length != prefix.Length + 32 ||
            !name[prefix.Length..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            !Guid.TryParseExact(name[prefix.Length..], "N", out var id) || id == Guid.Empty)
            throw new ArgumentException("Only a generated migration-test database name is allowed.", nameof(name));
        return provider switch
        {
            "PostgreSql" => $"\"{name}\"",
            "SqlServer" => $"[{name}]",
            "MySql" => $"`{name}`",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    // The execution seam exercises lifecycle failures without a provider, Docker, or connection secrets.
    internal static async Task RunExternalAsync(ExternalDatabasePlan database, Func<string, Task> run, Func<string, Action, Task> execute)
    {
        var created = false;
        Exception? failure = null;
        Exception? cleanupFailure = null;
        try
        {
            // Confirmation happens immediately after the server acknowledges CREATE, before disposing
            // its command/connection. A disposal failure must still clean up the now-owned database.
            await execute(database.CreateSql, () => created = true);
            if (!created) throw new InvalidOperationException("Database creation was not confirmed.");
            await run(database.ConnectionString);
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            // A failed unacknowledged CREATE does not establish ownership: never DROP a
            // possibly pre-existing database after a name collision or connection failure.
            if (created)
            {
                try { await execute(database.DropSql, () => { }); }
                catch (Exception exception) { cleanupFailure = exception; }
            }
        }

        if (failure is not null && cleanupFailure is not null)
            throw new AggregateException("The provider test and its isolated-database cleanup both failed.", failure, cleanupFailure);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    private static async Task ExecuteAdminAsync(ExternalDatabasePlan database, string sql, Action confirmed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var connection = database.CreateAdminConnection();
        await connection.OpenAsync(timeout.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(timeout.Token);
        confirmed();
    }

    internal sealed class ExternalDatabasePlan(string name, string connectionString, Func<DbConnection> createAdminConnection,
        string createSql, string dropSql)
    {
        public string Name { get; } = name;
        public string ConnectionString { get; } = connectionString;
        public string CreateSql { get; } = createSql;
        public string DropSql { get; } = dropSql;
        public DbConnection CreateAdminConnection() => createAdminConnection();
        // Deliberately keep connection strings out of this object's diagnostic representation.
        public override string ToString() => Name;
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
