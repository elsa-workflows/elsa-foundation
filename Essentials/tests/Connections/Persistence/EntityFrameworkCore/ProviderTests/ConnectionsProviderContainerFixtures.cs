using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class ConnectionsPostgreSqlContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string? _connectionString;

    public const string CollectionName = "connections-ef-postgresql";

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => _connectionString ?? _container?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL container is not available.");

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable(
            "ELSA_CONNECTIONS_EF_POSTGRESQL_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            IsAvailable = true;
            return;
        }

        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa_connections")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (ProviderContainerSupport.IsUnavailable(exception) && !ProviderContainerSupport.ProvidersRequired)
        {
            SkipReason = ProviderContainerSupport.SkipReason("PostgreSQL", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

public sealed class ConnectionsSqlServerContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    private string? _connectionString;

    public const string CollectionName = "connections-ef-sqlserver";

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => _connectionString ?? _container?.GetConnectionString()
        ?? throw new InvalidOperationException("SQL Server container is not available.");

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable(
            "ELSA_CONNECTIONS_EF_SQLSERVER_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            IsAvailable = true;
            return;
        }

        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04")
                .Build();
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (ProviderContainerSupport.IsUnavailable(exception) && !ProviderContainerSupport.ProvidersRequired)
        {
            SkipReason = ProviderContainerSupport.SkipReason("SQL Server", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

public sealed class ConnectionsMySqlContainerFixture : IAsyncLifetime
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? _container;
    private string? _connectionString;

    public const string CollectionName = "connections-ef-mysql";

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => _connectionString ?? _container?.GetConnectionString()
        ?? throw new InvalidOperationException("MySQL container is not available.");

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable(
            "ELSA_CONNECTIONS_EF_MYSQL_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            IsAvailable = true;
            return;
        }

        try
        {
            _container = new MySqlBuilder(Image)
                .WithDatabase("elsa_connections")
                .WithUsername("root")
                .WithPassword("root")
                .Build();
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (ProviderContainerSupport.IsUnavailable(exception) && !ProviderContainerSupport.ProvidersRequired)
        {
            SkipReason = ProviderContainerSupport.SkipReason("MySQL", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

internal static class ProviderContainerSupport
{
    public static bool ProvidersRequired => Environment.GetEnvironmentVariable("ELSA_CONNECTIONS_EF_REQUIRE_NATIVE_PROVIDERS") == "1";

    public static bool IsUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsUnavailable(exception.InnerException);

    public static string SkipReason(string provider, Exception exception) =>
        $"Docker/{provider} container unavailable: {exception.Message}";
}

[CollectionDefinition(ConnectionsPostgreSqlContainerFixture.CollectionName)]
public sealed class ConnectionsPostgreSqlCollection :
    ICollectionFixture<ConnectionsPostgreSqlContainerFixture>
{
}

[CollectionDefinition(ConnectionsSqlServerContainerFixture.CollectionName)]
public sealed class ConnectionsSqlServerCollection :
    ICollectionFixture<ConnectionsSqlServerContainerFixture>
{
}

[CollectionDefinition(ConnectionsMySqlContainerFixture.CollectionName)]
public sealed class ConnectionsMySqlCollection :
    ICollectionFixture<ConnectionsMySqlContainerFixture>
{
}
