using System.Data.Common;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Rules.Persistence.EntityFrameworkCore.ProviderTests;

public abstract class RulesNativeProviderFixture : IAsyncLifetime
{
    internal const string RequireNativeProvidersVariable = "ELSA_RULES_EF_REQUIRE_NATIVE_PROVIDERS";
    private IAsyncDisposable? _container;
    private string? _administrationConnectionString;

    protected abstract string Provider { get; }
    protected abstract string EnvironmentVariable { get; }
    protected virtual bool IsSqlite => false;

    protected abstract Task<(IAsyncDisposable Container, string ConnectionString)> StartContainerAsync();
    protected abstract DbConnection OpenAdministrationConnection(string connectionString);
    protected abstract string CreateDatabaseSql(string databaseName);
    protected abstract string DropDatabaseSql(string databaseName);
    protected abstract string BuildDatabaseConnectionString(string administrationConnectionString, string databaseName);
    protected abstract string BuildAdministrationConnectionString(string connectionString);

    internal bool IsAvailable { get; private set; }
    internal string? SkipReason { get; private set; }
    internal static bool RequireNativeProviders =>
        Environment.GetEnvironmentVariable(RequireNativeProvidersVariable) is "1" ||
        bool.TryParse(Environment.GetEnvironmentVariable(RequireNativeProvidersVariable), out var required) && required;

    public async Task InitializeAsync()
    {
        if (IsSqlite)
        {
            IsAvailable = true;
            return;
        }

        var supplied = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            _administrationConnectionString = supplied;
            IsAvailable = true;
            return;
        }

        try
        {
            (_container, _administrationConnectionString) = await StartContainerAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            SkipReason = $"Docker/{Provider} is unavailable for the Rules native-provider suite.";
            if (RequireNativeProviders)
                SkipReason += $" Set {EnvironmentVariable} to a disposable test server connection string or provide Docker.";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    internal void RequireAvailable()
    {
        if (IsAvailable)
            return;
        if (RequireNativeProviders)
            throw new InvalidOperationException(SkipReason ?? $"Rules {Provider} provider is required but unavailable.");
        Skip.If(true, SkipReason ?? $"Rules {Provider} provider is unavailable.");
    }

    internal async Task<RulesProviderDatabase> CreateDatabaseAsync()
    {
        RequireAvailable();
        if (IsSqlite)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"elsa-rules-provider-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "rules.db");
            return new(Provider, $"Data Source={path};Pooling=False", async () =>
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
                await Task.CompletedTask;
            });
        }

        var administration = _administrationConnectionString
            ?? throw new InvalidOperationException($"Rules {Provider} administration connection is unavailable.");
        var databaseName = $"elsa_rules_{Guid.NewGuid():N}";
        var created = false;
        try
        {
            await using (var connection = OpenAdministrationConnection(BuildAdministrationConnectionString(administration)))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = CreateDatabaseSql(databaseName);
                await command.ExecuteNonQueryAsync();
            }
            created = true;

            return new(Provider, BuildDatabaseConnectionString(administration, databaseName), async () =>
            {
                await using var connection = OpenAdministrationConnection(BuildAdministrationConnectionString(administration));
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = DropDatabaseSql(databaseName);
                await command.ExecuteNonQueryAsync();
            });
        }
        catch
        {
            if (created)
            {
                await using var connection = OpenAdministrationConnection(BuildAdministrationConnectionString(administration));
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = DropDatabaseSql(databaseName);
                await command.ExecuteNonQueryAsync();
            }
            throw;
        }
    }

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}

internal sealed class RulesProviderDatabase(string provider, string connectionString, Func<Task> cleanup) : IAsyncDisposable
{
    internal string Provider { get; } = provider;
    internal string ConnectionString { get; } = connectionString;

    public ValueTask DisposeAsync() => new(cleanup());
}

public sealed class RulesSqliteProviderFixture : RulesNativeProviderFixture
{
    protected override string Provider => "Sqlite";
    protected override string EnvironmentVariable => string.Empty;
    protected override bool IsSqlite => true;
    protected override Task<(IAsyncDisposable Container, string ConnectionString)> StartContainerAsync() =>
        throw new NotSupportedException();
    protected override DbConnection OpenAdministrationConnection(string connectionString) => throw new NotSupportedException();
    protected override string CreateDatabaseSql(string databaseName) => throw new NotSupportedException();
    protected override string DropDatabaseSql(string databaseName) => throw new NotSupportedException();
    protected override string BuildDatabaseConnectionString(string administrationConnectionString, string databaseName) => throw new NotSupportedException();
    protected override string BuildAdministrationConnectionString(string connectionString) => throw new NotSupportedException();
}

public sealed class RulesPostgreSqlProviderFixture : RulesNativeProviderFixture
{
    protected override string Provider => "PostgreSql";
    protected override string EnvironmentVariable => "ELSA_RULES_EF_POSTGRESQL_TEST_CONNECTION_STRING";

    protected override async Task<(IAsyncDisposable Container, string ConnectionString)> StartContainerAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("elsa_rules_admin")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        try
        {
            await container.StartAsync();
            return (container, container.GetConnectionString());
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    protected override DbConnection OpenAdministrationConnection(string connectionString) => new NpgsqlConnection(connectionString);
    protected override string CreateDatabaseSql(string databaseName) => $"CREATE DATABASE \"{databaseName}\"";
    protected override string DropDatabaseSql(string databaseName) => $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
    protected override string BuildDatabaseConnectionString(string connectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName, Pooling = false }.ConnectionString;
    protected override string BuildAdministrationConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
}

public sealed class RulesSqlServerProviderFixture : RulesNativeProviderFixture
{
    protected override string Provider => "SqlServer";
    protected override string EnvironmentVariable => "ELSA_RULES_EF_SQLSERVER_TEST_CONNECTION_STRING";

    protected override async Task<(IAsyncDisposable Container, string ConnectionString)> StartContainerAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        try
        {
            await container.StartAsync();
            return (container, container.GetConnectionString());
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    protected override DbConnection OpenAdministrationConnection(string connectionString) => new SqlConnection(connectionString);
    protected override string CreateDatabaseSql(string databaseName) => $"CREATE DATABASE [{databaseName}]";
    protected override string DropDatabaseSql(string databaseName) =>
        $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
    protected override string BuildDatabaseConnectionString(string connectionString, string databaseName) =>
        new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName, Pooling = false }.ConnectionString;
    protected override string BuildAdministrationConnectionString(string connectionString) =>
        new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", Pooling = false }.ConnectionString;
}

public sealed class RulesMySqlProviderFixture : RulesNativeProviderFixture
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    protected override string Provider => "MySql";
    protected override string EnvironmentVariable => "ELSA_RULES_EF_MYSQL_TEST_CONNECTION_STRING";

    protected override async Task<(IAsyncDisposable Container, string ConnectionString)> StartContainerAsync()
    {
        var container = new MySqlBuilder(Image)
            .WithDatabase("elsa_rules_admin")
            .WithUsername("root")
            .WithPassword("root")
            .Build();
        try
        {
            await container.StartAsync();
            return (container, new MySqlConnectionStringBuilder(container.GetConnectionString())
            {
                SslMode = MySqlSslMode.Disabled
            }.ConnectionString);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    protected override DbConnection OpenAdministrationConnection(string connectionString) => new MySqlConnection(connectionString);
    protected override string CreateDatabaseSql(string databaseName) => $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4";
    protected override string DropDatabaseSql(string databaseName) => $"DROP DATABASE IF EXISTS `{databaseName}`";
    protected override string BuildDatabaseConnectionString(string connectionString, string databaseName) =>
        new MySqlConnectionStringBuilder(connectionString) { Database = databaseName, Pooling = false }.ConnectionString;
    protected override string BuildAdministrationConnectionString(string connectionString) =>
        new MySqlConnectionStringBuilder(connectionString) { Database = string.Empty, Pooling = false }.ConnectionString;
}

[CollectionDefinition("rules-provider-sqlite", DisableParallelization = true)]
public sealed class RulesSqliteProviderCollection : ICollectionFixture<RulesSqliteProviderFixture>;

[CollectionDefinition("rules-provider-postgresql", DisableParallelization = true)]
public sealed class RulesPostgreSqlProviderCollection : ICollectionFixture<RulesPostgreSqlProviderFixture>;

[CollectionDefinition("rules-provider-sqlserver", DisableParallelization = true)]
public sealed class RulesSqlServerProviderCollection : ICollectionFixture<RulesSqlServerProviderFixture>;

[CollectionDefinition("rules-provider-mysql", DisableParallelization = true)]
public sealed class RulesMySqlProviderCollection : ICollectionFixture<RulesMySqlProviderFixture>;
