using DotNet.Testcontainers.Builders;
using Elsa.Cluster.EntityFrameworkCore.Testing;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Cluster.EntityFrameworkCore.ProviderTests;

/// <summary>
/// One database per engine, shared by that engine's suites, which run one after another in its collection. Each suite's
/// fixture empties the membership table before it starts. Without Docker the suites skip, unless
/// <see cref="RequireVariable"/> is set, in which case they fail: a green run is then native-provider evidence.
/// </summary>
public abstract class MembershipDatabase(string provider, string engine) : IAsyncLifetime
{
    public const string RequireVariable = "ELSA_CLUSTER_MEMBERSHIP_EF_REQUIRE_NATIVE_PROVIDERS";
    protected const string DatabaseName = "elsa_cluster_membership";
    private IAsyncDisposable? _container;

    public EfClusterMembershipTestStore Store { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            var (container, connectionString) = await StartAsync();
            _container = container;
            Store = new EfClusterMembershipTestStore(provider, connectionString);
        }
        catch (Exception exception) when (IsDockerUnavailable(exception) && Environment.GetEnvironmentVariable(RequireVariable) is not ("1" or "true"))
        {
            Store = EfClusterMembershipTestStore.Unavailable(provider, $"Docker/{engine} unavailable: {exception.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    protected abstract Task<(IAsyncDisposable Container, string ConnectionString)> StartAsync();

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}

public sealed class PostgreSqlMembershipDatabase() : MembershipDatabase("PostgreSql", "PostgreSQL")
{
    public const string Collection = "cluster-membership-ef-postgresql";

    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase(DatabaseName).Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    }
}

public sealed class SqlServerMembershipDatabase() : MembershipDatabase("SqlServer", "SQL Server")
{
    public const string Collection = "cluster-membership-ef-sqlserver";

    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();
        await using (var admin = new SqlConnection(container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{DatabaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        return (container, new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = DatabaseName }.ConnectionString);
    }
}

public sealed class MySqlMembershipDatabase() : MembershipDatabase("MySql", "MySQL")
{
    public const string Collection = "cluster-membership-ef-mysql";

    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new MySqlBuilder("mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a")
            .WithDatabase(DatabaseName).WithUsername("root").WithPassword("root").Build();
        await container.StartAsync();
        // No TLS against a throwaway container: MySql.Data negotiates it by default, which nothing here exercises.
        return (container, new MySqlConnectionStringBuilder(container.GetConnectionString()) { SslMode = MySqlSslMode.Disabled }.ConnectionString);
    }
}

[CollectionDefinition(PostgreSqlMembershipDatabase.Collection)]
public sealed class PostgreSqlMembershipCollection : ICollectionFixture<PostgreSqlMembershipDatabase>;

[CollectionDefinition(SqlServerMembershipDatabase.Collection)]
public sealed class SqlServerMembershipCollection : ICollectionFixture<SqlServerMembershipDatabase>;

[CollectionDefinition(MySqlMembershipDatabase.Collection)]
public sealed class MySqlMembershipCollection : ICollectionFixture<MySqlMembershipDatabase>;
