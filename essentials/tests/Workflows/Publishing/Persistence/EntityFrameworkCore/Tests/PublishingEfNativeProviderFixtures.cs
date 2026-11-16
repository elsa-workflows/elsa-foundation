using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class PublishingPostgreSqlContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private string? connectionString;

    public const string CollectionName = "publishing-ef-postgresql";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => connectionString ?? container?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL is unavailable.");

    public async Task InitializeAsync()
    {
        connectionString = Environment.GetEnvironmentVariable("ELSA_PUBLISHING_EF_POSTGRESQL_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            IsAvailable = true;
            return;
        }

        try
        {
            container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa_publishing")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (PublishingProviderContainerSupport.IsUnavailable(exception))
        {
            SkipReason = PublishingProviderContainerSupport.SkipReason("PostgreSQL", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class PublishingSqlServerContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? container;
    private string? connectionString;

    public const string CollectionName = "publishing-ef-sqlserver";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => connectionString ?? container?.GetConnectionString()
        ?? throw new InvalidOperationException("SQL Server is unavailable.");

    public async Task InitializeAsync()
    {
        connectionString = Environment.GetEnvironmentVariable("ELSA_PUBLISHING_EF_SQLSERVER_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            IsAvailable = true;
            return;
        }

        try
        {
            container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04")
                .Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (PublishingProviderContainerSupport.IsUnavailable(exception))
        {
            SkipReason = PublishingProviderContainerSupport.SkipReason("SQL Server", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class PublishingMySqlContainerFixture : IAsyncLifetime
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? container;
    private string? connectionString;

    public const string CollectionName = "publishing-ef-mysql";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => connectionString ?? container?.GetConnectionString()
        ?? throw new InvalidOperationException("MySQL is unavailable.");

    public async Task InitializeAsync()
    {
        connectionString = Environment.GetEnvironmentVariable("ELSA_PUBLISHING_EF_MYSQL_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            IsAvailable = true;
            return;
        }

        try
        {
            container = new MySqlBuilder(Image)
                .WithDatabase("elsa_publishing")
                .WithUsername("root")
                .WithPassword("root")
                .Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (PublishingProviderContainerSupport.IsUnavailable(exception))
        {
            SkipReason = PublishingProviderContainerSupport.SkipReason("MySQL", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

internal static class PublishingProviderContainerSupport
{
    public static bool RequireNativeProviderMatrix =>
        Environment.GetEnvironmentVariable("ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX") is "1" or "true";

    /// <summary>
    /// The provider's connection string. An unavailable provider skips the test, or fails it when the native
    /// provider matrix is required, so a missing provider is never reported as a pass.
    /// </summary>
    public static string Require(bool isAvailable, string? skipReason, string provider, Func<string> connectionString)
    {
        var reason = skipReason ?? $"{provider} is unavailable.";
        if (RequireNativeProviderMatrix)
            Assert.True(isAvailable, reason);
        Skip.IfNot(isAvailable, reason);
        return connectionString();
    }

    public static bool IsUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsUnavailable(exception.InnerException);

    public static string SkipReason(string provider, Exception exception) =>
        $"Docker/{provider} unavailable: {exception.Message}";
}

[CollectionDefinition(PublishingPostgreSqlContainerFixture.CollectionName)]
public sealed class PublishingPostgreSqlCollection : ICollectionFixture<PublishingPostgreSqlContainerFixture>;

[CollectionDefinition(PublishingSqlServerContainerFixture.CollectionName)]
public sealed class PublishingSqlServerCollection : ICollectionFixture<PublishingSqlServerContainerFixture>;

[CollectionDefinition(PublishingMySqlContainerFixture.CollectionName)]
public sealed class PublishingMySqlCollection : ICollectionFixture<PublishingMySqlContainerFixture>;
