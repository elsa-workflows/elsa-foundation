using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.ProviderTests;

public abstract class StructuredLogsProviderFixture : IAsyncLifetime
{
    private protected string? ConnectionStringValue;

    public bool IsAvailable { get; protected set; }
    public string? SkipReason { get; protected set; }
    public string ConnectionString => ConnectionStringValue ?? throw new InvalidOperationException("Provider is unavailable.");

    protected abstract string EnvironmentVariable { get; }
    protected abstract Task StartContainerAsync();
    protected abstract Task DisposeContainerAsync();

    public async Task InitializeAsync()
    {
        ConnectionStringValue = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(ConnectionStringValue))
        {
            IsAvailable = true;
            return;
        }

        try
        {
            await StartContainerAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            SkipReason = $"Docker provider container unavailable: {exception.Message}";
        }
    }

    public Task DisposeAsync() => DisposeContainerAsync();

    protected static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}

public sealed class StructuredLogsPostgreSqlFixture : StructuredLogsProviderFixture
{
    private PostgreSqlContainer? container;
    protected override string EnvironmentVariable => "ELSA_STRUCTURED_LOGS_EF_POSTGRESQL_TEST_CONNECTION_STRING";
    public const string CollectionName = "structured-logs-ef-postgresql";

    protected override async Task StartContainerAsync()
    {
        container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("elsa_structured_logs")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await container.StartAsync();
        ConnectionStringValue = container.GetConnectionString();
    }

    protected override async Task DisposeContainerAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class StructuredLogsSqlServerFixture : StructuredLogsProviderFixture
{
    private MsSqlContainer? container;
    protected override string EnvironmentVariable => "ELSA_STRUCTURED_LOGS_EF_SQLSERVER_TEST_CONNECTION_STRING";
    public const string CollectionName = "structured-logs-ef-sqlserver";

    protected override async Task StartContainerAsync()
    {
        container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();
        ConnectionStringValue = container.GetConnectionString();
    }

    protected override async Task DisposeContainerAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class StructuredLogsMySqlFixture : StructuredLogsProviderFixture
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? container;
    protected override string EnvironmentVariable => "ELSA_STRUCTURED_LOGS_EF_MYSQL_TEST_CONNECTION_STRING";
    public const string CollectionName = "structured-logs-ef-mysql";

    protected override async Task StartContainerAsync()
    {
        container = new MySqlBuilder(Image)
            .WithDatabase("elsa_structured_logs")
            .WithUsername("root")
            .WithPassword("root")
            .Build();
        await container.StartAsync();
        ConnectionStringValue = container.GetConnectionString();
    }

    protected override async Task DisposeContainerAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

[CollectionDefinition(StructuredLogsPostgreSqlFixture.CollectionName)]
public sealed class StructuredLogsPostgreSqlCollection : ICollectionFixture<StructuredLogsPostgreSqlFixture>;

[CollectionDefinition(StructuredLogsSqlServerFixture.CollectionName)]
public sealed class StructuredLogsSqlServerCollection : ICollectionFixture<StructuredLogsSqlServerFixture>;

[CollectionDefinition(StructuredLogsMySqlFixture.CollectionName)]
public sealed class StructuredLogsMySqlCollection : ICollectionFixture<StructuredLogsMySqlFixture>;
