using System.Security.Cryptography;
using System.Data.Common;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Etl.Persistence.ProviderTests;

/// <summary>
/// A real database server for the ETL EF provider suite: a supplied connection string wins, otherwise a container
/// starts. Each test gets its own database. Without Docker the suite skips, unless <see cref="RequireNativeProvidersVariable"/>
/// makes it fail, as CI does.
/// </summary>
public abstract class EtlNativeProviderFixture : IAsyncLifetime
{
    internal const string RequireNativeProvidersVariable = "ELSA_ETL_EF_REQUIRE_NATIVE_PROVIDERS";
    private IAsyncDisposable? _container;
    private string? _administrationConnectionString;

    internal abstract string Provider { get; }
    protected abstract string EnvironmentVariable { get; }

    protected abstract Task<(IAsyncDisposable Container, string ConnectionString)> StartContainerAsync();
    protected abstract DbConnection OpenAdministrationConnection(string connectionString);
    protected abstract string CreateDatabaseSql(string databaseName);
    protected abstract string DropDatabaseSql(string databaseName);
    protected abstract string BuildDatabaseConnectionString(string administrationConnectionString, string databaseName);
    protected abstract string BuildAdministrationConnectionString(string connectionString);
    /// <summary>A query returning the collation the server records for one column of the database it runs in.</summary>
    internal abstract string ColumnCollationSql(string table, string column);

    internal bool IsAvailable { get; private set; }
    internal string? SkipReason { get; private set; }
    internal static bool RequireNativeProviders =>
        Environment.GetEnvironmentVariable(RequireNativeProvidersVariable) is "1" ||
        bool.TryParse(Environment.GetEnvironmentVariable(RequireNativeProvidersVariable), out var required) && required;

    public async Task InitializeAsync()
    {

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
            SkipReason = $"Docker/{Provider} is unavailable for the ETL EF provider suite.";
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
            throw new InvalidOperationException(SkipReason ?? $"ETL {Provider} provider is required but unavailable.");
        Skip.If(true, SkipReason ?? $"ETL {Provider} provider is unavailable.");
    }

    internal async Task<EtlProviderDatabase> CreateDatabaseAsync()
    {
        RequireAvailable();

        var administration = _administrationConnectionString
            ?? throw new InvalidOperationException($"ETL {Provider} administration connection is unavailable.");
        var databaseName = $"elsa_etl_{Guid.NewGuid():N}";
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

internal sealed class EtlProviderDatabase(string provider, string connectionString, Func<Task> cleanup) : IAsyncDisposable
{
    internal string Provider { get; } = provider;
    internal string ConnectionString { get; } = connectionString;

    public ValueTask DisposeAsync() => new(cleanup());
}

public sealed class EtlPostgreSqlProviderFixture : EtlNativeProviderFixture
{
    internal override string Provider => "PostgreSql";
    protected override string EnvironmentVariable => "ELSA_ETL_EF_POSTGRESQL_TEST_CONNECTION_STRING";

    protected override async Task<(IAsyncDisposable Container, string ConnectionString)> StartContainerAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("elsa_etl_admin")
            .WithUsername("postgres")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "!aA1")
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
    internal override string ColumnCollationSql(string table, string column) =>
        $"SELECT collation_name FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}'";
}

public sealed class EtlSqlServerProviderFixture : EtlNativeProviderFixture
{
    internal override string Provider => "SqlServer";
    protected override string EnvironmentVariable => "ELSA_ETL_EF_SQLSERVER_TEST_CONNECTION_STRING";

    protected override async Task<(IAsyncDisposable Container, string ConnectionString)> StartContainerAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "!aA1")
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

    protected override DbConnection OpenAdministrationConnection(string connectionString) => new SqlConnection(connectionString);
    protected override string CreateDatabaseSql(string databaseName) => $"CREATE DATABASE [{databaseName}]";
    protected override string DropDatabaseSql(string databaseName) =>
        $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
    protected override string BuildDatabaseConnectionString(string connectionString, string databaseName) =>
        new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName, Pooling = false }.ConnectionString;
    protected override string BuildAdministrationConnectionString(string connectionString) =>
        new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", Pooling = false }.ConnectionString;
    internal override string ColumnCollationSql(string table, string column) =>
        $"SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID('{table}') AND name = '{column}'";
}

public sealed class EtlMySqlProviderFixture : EtlNativeProviderFixture
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    internal override string Provider => "MySql";
    protected override string EnvironmentVariable => "ELSA_ETL_EF_MYSQL_TEST_CONNECTION_STRING";

    protected override async Task<(IAsyncDisposable Container, string ConnectionString)> StartContainerAsync()
    {
        var container = new MySqlBuilder(Image)
            .WithDatabase("elsa_etl_admin")
            .WithUsername("root")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "!aA1")
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
    internal override string ColumnCollationSql(string table, string column) =>
        $"SELECT COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'";
}

[CollectionDefinition(EtlPostgreSqlProviderCollection.Name, DisableParallelization = true)]
public sealed class EtlPostgreSqlProviderCollection : ICollectionFixture<EtlPostgreSqlProviderFixture> { public const string Name = "etl-ef-postgresql"; }

[CollectionDefinition(EtlSqlServerProviderCollection.Name, DisableParallelization = true)]
public sealed class EtlSqlServerProviderCollection : ICollectionFixture<EtlSqlServerProviderFixture> { public const string Name = "etl-ef-sqlserver"; }

[CollectionDefinition(EtlMySqlProviderCollection.Name, DisableParallelization = true)]
public sealed class EtlMySqlProviderCollection : ICollectionFixture<EtlMySqlProviderFixture> { public const string Name = "etl-ef-mysql"; }
