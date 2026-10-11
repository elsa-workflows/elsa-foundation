using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class RuntimePlacementPostgreSqlContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;

    public const string CollectionName = "runtime-placement-ef-postgresql";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => container?.GetConnectionString() ?? throw new InvalidOperationException("PostgreSQL container is not available.");

    public async Task InitializeAsync()
    {
        try
        {
            container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa_runtime_placement")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (RuntimePlacementContainerSupport.IsUnavailable(exception))
        {
            SkipReason = RuntimePlacementContainerSupport.SkipReasonOrThrow("PostgreSQL", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class RuntimePlacementSqlServerContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? container;

    public const string CollectionName = "runtime-placement-ef-sqlserver";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => container?.GetConnectionString() ?? throw new InvalidOperationException("SQL Server container is not available.");

    public async Task InitializeAsync()
    {
        try
        {
            container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (RuntimePlacementContainerSupport.IsUnavailable(exception))
        {
            SkipReason = RuntimePlacementContainerSupport.SkipReasonOrThrow("SQL Server", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class RuntimePlacementMySqlContainerFixture : IAsyncLifetime
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? container;

    public const string CollectionName = "runtime-placement-ef-mysql";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => MySqlTestConnection.WithoutSsl(
        container?.GetConnectionString() ?? throw new InvalidOperationException("MySQL container is not available."));

    public async Task InitializeAsync()
    {
        try
        {
            container = new MySqlBuilder(Image)
                .WithDatabase("elsa_runtime_placement")
                .WithUsername("root")
                .WithPassword("root")
                .Build();
            await container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (RuntimePlacementContainerSupport.IsUnavailable(exception))
        {
            SkipReason = RuntimePlacementContainerSupport.SkipReasonOrThrow("MySQL", exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

internal static class RuntimePlacementContainerSupport
{
    private const string RequireNativeProvidersVariable = "ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS";

    public static bool IsUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsUnavailable(exception.InnerException);

    public static string SkipReasonOrThrow(string provider, Exception exception)
    {
        var message = $"Docker/{provider} container unavailable: {exception.Message}";
        if (Environment.GetEnvironmentVariable(RequireNativeProvidersVariable) is "1" or "true")
            throw new InvalidOperationException($"Required Runtime execution-placement EF provider evidence is unavailable. {message}", exception);
        return message;
    }
}

[CollectionDefinition(RuntimePlacementPostgreSqlContainerFixture.CollectionName)]
public sealed class RuntimePlacementPostgreSqlCollection : ICollectionFixture<RuntimePlacementPostgreSqlContainerFixture>
{
}

[CollectionDefinition(RuntimePlacementSqlServerContainerFixture.CollectionName)]
public sealed class RuntimePlacementSqlServerCollection : ICollectionFixture<RuntimePlacementSqlServerContainerFixture>
{
}

[CollectionDefinition(RuntimePlacementMySqlContainerFixture.CollectionName)]
public sealed class RuntimePlacementMySqlCollection : ICollectionFixture<RuntimePlacementMySqlContainerFixture>
{
}
