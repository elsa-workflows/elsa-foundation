using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>Owns one isolated provider database for each provider smoke class.</summary>
public abstract class OpenTelemetryProviderFixture : IAsyncLifetime
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
        catch (Exception exception) when (ProviderContainerSupport.IsUnavailable(exception))
        {
            SkipReason = ProviderContainerSupport.SkipReason(exception);
        }
    }

    public Task DisposeAsync() => DisposeContainerAsync();
}

public sealed class OpenTelemetryPostgreSqlFixture : OpenTelemetryProviderFixture
{
    private PostgreSqlContainer? container;
    protected override string EnvironmentVariable => "ELSA_OPEN_TELEMETRY_EF_POSTGRESQL_TEST_CONNECTION_STRING";
    public const string CollectionName = "open-telemetry-ef-postgresql";

    protected override async Task StartContainerAsync()
    {
        var database = "elsa_otel_" + Guid.NewGuid().ToString("N");
        container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase(database)
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

public sealed class OpenTelemetrySqlServerFixture : OpenTelemetryProviderFixture
{
    private MsSqlContainer? container;
    protected override string EnvironmentVariable => "ELSA_OPEN_TELEMETRY_EF_SQLSERVER_TEST_CONNECTION_STRING";
    public const string CollectionName = "open-telemetry-ef-sqlserver";

    protected override async Task StartContainerAsync()
    {
        container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();

        var database = "elsa_otel_" + Guid.NewGuid().ToString("N");
        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{database}]";
        await command.ExecuteNonQueryAsync();

        ConnectionStringValue = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = database
        }.ConnectionString;
    }

    protected override async Task DisposeContainerAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class OpenTelemetryMySqlFixture : OpenTelemetryProviderFixture
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? container;
    protected override string EnvironmentVariable => "ELSA_OPEN_TELEMETRY_EF_MYSQL_TEST_CONNECTION_STRING";
    public const string CollectionName = "open-telemetry-ef-mysql";

    protected override async Task StartContainerAsync()
    {
        container = new MySqlBuilder(Image)
            .WithDatabase("elsa_otel_" + Guid.NewGuid().ToString("N"))
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

internal static class ProviderContainerSupport
{
    public static bool IsUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.GetType().Name.Contains("Image", StringComparison.OrdinalIgnoreCase) ||
        exception.GetType().Name.Contains("Container", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsUnavailable(exception.InnerException);

    public static string SkipReason(Exception exception) =>
        $"Docker/provider container unavailable: {exception.Message}";
}

[CollectionDefinition(OpenTelemetryPostgreSqlFixture.CollectionName)]
public sealed class OpenTelemetryPostgreSqlCollection : ICollectionFixture<OpenTelemetryPostgreSqlFixture>;

[CollectionDefinition(OpenTelemetrySqlServerFixture.CollectionName)]
public sealed class OpenTelemetrySqlServerCollection : ICollectionFixture<OpenTelemetrySqlServerFixture>;

[CollectionDefinition(OpenTelemetryMySqlFixture.CollectionName)]
public sealed class OpenTelemetryMySqlCollection : ICollectionFixture<OpenTelemetryMySqlFixture>;
