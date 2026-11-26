using System.Data.Common;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Activities.Compensation.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// One server per provider for the compensation provider subset. A connection string in the provider's environment
/// variable is used as it is; otherwise a Testcontainers server starts. Each journey starts whole host generations whose
/// module migrator installs the runtime schema, so each gets an empty database of its own on this server. With
/// <see cref="RequireNativeProvidersVariable"/> set, as the nightly <c>compensation</c> EF suite sets it, an unavailable
/// server fails the suite instead of skipping it.
/// </summary>
public abstract class CompensationProviderFixture : IAsyncLifetime
{
    public const string RequireNativeProvidersVariable = "ELSA_COMPENSATION_EF_REQUIRE_NATIVE_PROVIDERS";

    private string? _connectionString;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    /// <summary>The runtime EF provider name the subset configures.</summary>
    public abstract string Provider { get; }

    protected abstract string EnvironmentVariable { get; }
    protected abstract Task<string> StartContainerAsync();
    protected abstract Task DisposeContainerAsync();
    protected abstract DbConnection OpenServerConnection(string connectionString);
    protected abstract string CreateDatabaseSql(string database);

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            IsAvailable = true;
            return;
        }

        try
        {
            _connectionString = await StartContainerAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            var message = $"Docker container unavailable for {EnvironmentVariable}: {exception.Message}";
            if (Environment.GetEnvironmentVariable(RequireNativeProvidersVariable) is "1" or "true")
                throw new InvalidOperationException($"Required compensation EF provider evidence is unavailable. {message}", exception);
            SkipReason = message;
        }
    }

    public Task DisposeAsync() => DisposeContainerAsync();

    /// <summary>Creates an empty database on this server and returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var database = $"elsa_compensation_{Guid.NewGuid():N}"[..40];
        await using (var connection = OpenServerConnection(_connectionString ?? throw new InvalidOperationException("Provider is unavailable.")))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = CreateDatabaseSql(database);
            await command.ExecuteNonQueryAsync();
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = _connectionString };
        builder["Database"] = database;
        return builder.ConnectionString;
    }

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}

public sealed class CompensationPostgreSqlFixture : CompensationProviderFixture
{
    public const string CollectionName = "compensation-ef-postgresql";
    private PostgreSqlContainer? _container;

    public override string Provider => "PostgreSql";
    protected override string EnvironmentVariable => "ELSA_COMPENSATION_EF_POSTGRESQL_TEST_CONNECTION_STRING";

    protected override async Task<string> StartContainerAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("elsa_compensation")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    protected override async Task DisposeContainerAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    protected override DbConnection OpenServerConnection(string connectionString) => new NpgsqlConnection(connectionString);
    protected override string CreateDatabaseSql(string database) => $"CREATE DATABASE {database}";
}

public sealed class CompensationSqlServerFixture : CompensationProviderFixture
{
    public const string CollectionName = "compensation-ef-sqlserver";
    private MsSqlContainer? _container;

    public override string Provider => "SqlServer";
    protected override string EnvironmentVariable => "ELSA_COMPENSATION_EF_SQLSERVER_TEST_CONNECTION_STRING";

    protected override async Task<string> StartContainerAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    protected override async Task DisposeContainerAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    protected override DbConnection OpenServerConnection(string connectionString) => new SqlConnection(connectionString);
    protected override string CreateDatabaseSql(string database) => $"CREATE DATABASE [{database}]";
}

public sealed class CompensationMySqlFixture : CompensationProviderFixture
{
    public const string CollectionName = "compensation-ef-mysql";
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? _container;

    public override string Provider => "MySql";
    protected override string EnvironmentVariable => "ELSA_COMPENSATION_EF_MYSQL_TEST_CONNECTION_STRING";

    protected override async Task<string> StartContainerAsync()
    {
        _container = new MySqlBuilder(Image)
            .WithDatabase("elsa_compensation")
            .WithUsername("root")
            .WithPassword("root")
            .Build();
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    protected override async Task DisposeContainerAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    protected override DbConnection OpenServerConnection(string connectionString) => new MySqlConnection(connectionString);
    protected override string CreateDatabaseSql(string database) => $"CREATE DATABASE `{database}`";
}

[CollectionDefinition(CompensationPostgreSqlFixture.CollectionName)]
public sealed class CompensationPostgreSqlCollection : ICollectionFixture<CompensationPostgreSqlFixture>;

[CollectionDefinition(CompensationSqlServerFixture.CollectionName)]
public sealed class CompensationSqlServerCollection : ICollectionFixture<CompensationSqlServerFixture>;

[CollectionDefinition(CompensationMySqlFixture.CollectionName)]
public sealed class CompensationMySqlCollection : ICollectionFixture<CompensationMySqlFixture>;
