using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// A native provider for the import smoke. A connection-string environment variable selects an existing
/// server; otherwise a Testcontainers instance is started. With
/// <c>GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX=1</c> an unavailable provider fails instead of skipping.
/// </summary>
public abstract class Elsa3ImportProviderFixture : IAsyncLifetime
{
    private protected string? ConnectionStringValue;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => ConnectionStringValue ?? throw new InvalidOperationException("The provider is unavailable.");

    public static bool RequireNativeProviderMatrix =>
        Environment.GetEnvironmentVariable("GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX") is "1" or "true";

    protected abstract string ProviderName { get; }
    protected abstract string EnvironmentVariable { get; }
    protected abstract Task StartContainerAsync();
    protected abstract Task DisposeContainerAsync();

    /// <summary>A connection string for a database no other test run has touched.</summary>
    public abstract Task<string> CreateIsolatedDatabaseAsync();

    public void RequireAvailable()
    {
        if (RequireNativeProviderMatrix)
            Assert.True(IsAvailable, SkipReason ?? $"{ProviderName} is unavailable.");
        Skip.IfNot(IsAvailable, SkipReason ?? $"{ProviderName} is unavailable.");
    }

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
        catch (Exception exception) when (IsUnavailable(exception))
        {
            SkipReason = $"Docker/{ProviderName} unavailable: {exception.Message}";
        }
    }

    public Task DisposeAsync() => DisposeContainerAsync();

    private static bool IsUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsUnavailable(exception.InnerException);
}

public sealed class Elsa3ImportPostgreSqlFixture : Elsa3ImportProviderFixture
{
    private PostgreSqlContainer? container;

    public const string CollectionName = "elsa3-import-ef-postgresql";
    protected override string ProviderName => "PostgreSQL";
    protected override string EnvironmentVariable => "ELSA3_IMPORT_EF_POSTGRESQL_TEST_CONNECTION_STRING";

    protected override async Task StartContainerAsync()
    {
        container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("elsa3_import")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await container.StartAsync();
        ConnectionStringValue = container.GetConnectionString();
    }

    public override async Task<string> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"elsa3_import_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;
    }

    protected override async Task DisposeContainerAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class Elsa3ImportSqlServerFixture : Elsa3ImportProviderFixture
{
    private MsSqlContainer? container;

    public const string CollectionName = "elsa3-import-ef-sqlserver";
    protected override string ProviderName => "SQL Server";
    protected override string EnvironmentVariable => "ELSA3_IMPORT_EF_SQLSERVER_TEST_CONNECTION_STRING";

    protected override async Task StartContainerAsync()
    {
        container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();
        ConnectionStringValue = container.GetConnectionString();
    }

    public override async Task<string> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"elsa3_import_{Guid.NewGuid():N}";
        await using var admin = new SqlConnection(ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
        return new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = databaseName }.ConnectionString;
    }

    protected override async Task DisposeContainerAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

public sealed class Elsa3ImportMySqlFixture : Elsa3ImportProviderFixture
{
    private const string Image = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a";
    private MySqlContainer? container;

    public const string CollectionName = "elsa3-import-ef-mysql";
    protected override string ProviderName => "MySQL";
    protected override string EnvironmentVariable => "ELSA3_IMPORT_EF_MYSQL_TEST_CONNECTION_STRING";

    protected override async Task StartContainerAsync()
    {
        container = new MySqlBuilder(Image)
            .WithDatabase("elsa3_import")
            .WithUsername("root")
            .WithPassword("root")
            .Build();
        await container.StartAsync();
        ConnectionStringValue = container.GetConnectionString();
    }

    // The fixture owns a fresh container whose configured database is already isolated.
    public override Task<string> CreateIsolatedDatabaseAsync() => Task.FromResult(ConnectionString);

    protected override async Task DisposeContainerAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }
}

[CollectionDefinition(Elsa3ImportPostgreSqlFixture.CollectionName, DisableParallelization = true)]
public sealed class Elsa3ImportPostgreSqlCollection : ICollectionFixture<Elsa3ImportPostgreSqlFixture>;

[CollectionDefinition(Elsa3ImportSqlServerFixture.CollectionName, DisableParallelization = true)]
public sealed class Elsa3ImportSqlServerCollection : ICollectionFixture<Elsa3ImportSqlServerFixture>;

[CollectionDefinition(Elsa3ImportMySqlFixture.CollectionName, DisableParallelization = true)]
public sealed class Elsa3ImportMySqlCollection : ICollectionFixture<Elsa3ImportMySqlFixture>;
