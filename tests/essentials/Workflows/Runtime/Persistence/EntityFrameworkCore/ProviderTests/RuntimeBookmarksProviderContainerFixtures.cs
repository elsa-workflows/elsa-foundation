using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

public abstract class RuntimeBookmarksProviderFixture : IAsyncLifetime
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
        catch (Exception exception) when (RuntimeBookmarksProviderContainerSupport.IsUnavailable(exception))
        {
            SkipReason = RuntimeBookmarksProviderContainerSupport.SkipReasonOrThrow(EnvironmentVariable, exception);
        }
    }

    public Task DisposeAsync() => DisposeContainerAsync();
}

public sealed class RuntimeBookmarksPostgreSqlFixture : RuntimeBookmarksProviderFixture
{
    private PostgreSqlContainer? container;

    public const string CollectionName = "runtime-bookmarks-ef-postgresql";
    protected override string EnvironmentVariable => "ELSA_RUNTIME_BOOKMARKS_EF_POSTGRESQL_TEST_CONNECTION_STRING";

    protected override async Task StartContainerAsync()
    {
        container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("elsa_runtime_bookmarks")
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

public sealed class RuntimeBookmarksSqlServerFixture : RuntimeBookmarksProviderFixture
{
    private MsSqlContainer? container;

    public const string CollectionName = "runtime-bookmarks-ef-sqlserver";
    protected override string EnvironmentVariable => "ELSA_RUNTIME_BOOKMARKS_EF_SQLSERVER_TEST_CONNECTION_STRING";

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

public sealed class RuntimeBookmarksMySqlFixture : RuntimeBookmarksProviderFixture
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? container;

    public const string CollectionName = "runtime-bookmarks-ef-mysql";
    protected override string EnvironmentVariable => "ELSA_RUNTIME_BOOKMARKS_EF_MYSQL_TEST_CONNECTION_STRING";

    protected override async Task StartContainerAsync()
    {
        container = new MySqlBuilder(Image)
            .WithDatabase("elsa_runtime_bookmarks")
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

internal static class RuntimeBookmarksProviderContainerSupport
{
    // This is the strict gate already used by the Runtime EF native-provider lane.
    private const string RequireNativeProvidersVariable = "ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS";

    public static bool IsUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsUnavailable(exception.InnerException);

    public static string SkipReasonOrThrow(string providerVariable, Exception exception)
    {
        var message = $"Docker container unavailable for {providerVariable}: {exception.Message}";
        if (Environment.GetEnvironmentVariable(RequireNativeProvidersVariable) is "1" or "true")
            throw new InvalidOperationException($"Required Runtime bookmark EF provider evidence is unavailable. {message}", exception);
        return message;
    }
}

[CollectionDefinition(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeBookmarksPostgreSqlCollection : ICollectionFixture<RuntimeBookmarksPostgreSqlFixture>;

[CollectionDefinition(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeBookmarksSqlServerCollection : ICollectionFixture<RuntimeBookmarksSqlServerFixture>;

[CollectionDefinition(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeBookmarksMySqlCollection : ICollectionFixture<RuntimeBookmarksMySqlFixture>;
