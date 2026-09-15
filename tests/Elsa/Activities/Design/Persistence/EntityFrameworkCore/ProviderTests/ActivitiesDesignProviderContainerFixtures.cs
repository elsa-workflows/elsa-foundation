using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.ProviderTests;

public abstract class ActivitiesDesignProviderFixture : IAsyncLifetime
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
        catch (Exception exception) when (ActivitiesDesignProviderContainerSupport.IsUnavailable(exception))
        {
            SkipReason = ActivitiesDesignProviderContainerSupport.SkipReasonOrThrow(EnvironmentVariable, exception);
        }
    }

    public Task DisposeAsync() => DisposeContainerAsync();
}

public sealed class ActivitiesDesignPostgreSqlFixture : ActivitiesDesignProviderFixture
{
    private PostgreSqlContainer? container;

    public const string CollectionName = "activities-design-ef-postgresql";
    protected override string EnvironmentVariable => "ELSA_ACTIVITIES_DESIGN_EF_POSTGRESQL_TEST_CONNECTION_STRING";

    protected override async Task StartContainerAsync()
    {
        container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("elsa_activities_design")
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

public sealed class ActivitiesDesignSqlServerFixture : ActivitiesDesignProviderFixture
{
    private MsSqlContainer? container;

    public const string CollectionName = "activities-design-ef-sqlserver";
    protected override string EnvironmentVariable => "ELSA_ACTIVITIES_DESIGN_EF_SQLSERVER_TEST_CONNECTION_STRING";

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

public sealed class ActivitiesDesignMySqlFixture : ActivitiesDesignProviderFixture
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? container;

    public const string CollectionName = "activities-design-ef-mysql";
    protected override string EnvironmentVariable => "ELSA_ACTIVITIES_DESIGN_EF_MYSQL_TEST_CONNECTION_STRING";

    protected override async Task StartContainerAsync()
    {
        container = new MySqlBuilder(Image)
            .WithDatabase("elsa_activities_design")
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

internal static class ActivitiesDesignProviderContainerSupport
{
    private const string RequireNativeProvidersVariable = "ELSA_ACTIVITIES_DESIGN_EF_REQUIRE_NATIVE_PROVIDERS";

    public static bool IsUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsUnavailable(exception.InnerException);

    public static string SkipReasonOrThrow(string providerVariable, Exception exception)
    {
        var message = $"Docker container unavailable for {providerVariable}: {exception.Message}";
        if (Environment.GetEnvironmentVariable(RequireNativeProvidersVariable) is "1" or "true")
            throw new InvalidOperationException($"Required Activities Design EF provider evidence is unavailable. {message}", exception);
        return message;
    }
}

[CollectionDefinition(ActivitiesDesignPostgreSqlFixture.CollectionName, DisableParallelization = true)]
public sealed class ActivitiesDesignPostgreSqlCollection : ICollectionFixture<ActivitiesDesignPostgreSqlFixture>;

[CollectionDefinition(ActivitiesDesignSqlServerFixture.CollectionName, DisableParallelization = true)]
public sealed class ActivitiesDesignSqlServerCollection : ICollectionFixture<ActivitiesDesignSqlServerFixture>;

[CollectionDefinition(ActivitiesDesignMySqlFixture.CollectionName, DisableParallelization = true)]
public sealed class ActivitiesDesignMySqlCollection : ICollectionFixture<ActivitiesDesignMySqlFixture>;
