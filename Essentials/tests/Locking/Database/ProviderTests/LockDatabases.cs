using System.Data.Common;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Elsa.Locking.Database.ProviderTests;

/// <summary>
/// One database per engine, shared by that engine's suite. Without Docker the suites skip, unless <see cref="RequireVariable"/>
/// is set, in which case they fail: a green run is then native-provider evidence.
/// </summary>
public abstract class LockDatabase(string provider, string engine) : IAsyncLifetime
{
    public const string RequireVariable = "ELSA_LOCKING_DATABASE_REQUIRE_NATIVE_PROVIDERS";
    private IAsyncDisposable? _container;

    /// <summary>The engine, as <c>DatabaseDistributedLocking:Provider</c> names it.</summary>
    public string Provider => provider;

    public string ConnectionString { get; private set; } = "";

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            var (container, connectionString) = await StartAsync();
            _container = container;
            ConnectionString = connectionString;
        }
        catch (Exception exception) when (IsDockerUnavailable(exception) && Environment.GetEnvironmentVariable(RequireVariable) is not ("1" or "true"))
        {
            SkipReason = $"Docker/{engine} unavailable: {exception.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>Ends every session but this one that holds an application lock, as a failover or a network cut would.</summary>
    public async Task KillLockHoldersAsync()
    {
        await using var connection = CreateAdminConnection();
        await connection.OpenAsync();
        var sessions = new List<object>();
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = FindLockHoldersSql;
            await using var reader = await find.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                sessions.Add(reader.GetValue(0));
        }

        Assert.NotEmpty(sessions);
        foreach (var session in sessions)
        {
            await using var kill = connection.CreateCommand();
            kill.CommandText = KillSql(session);
            await kill.ExecuteNonQueryAsync();
        }
    }

    protected abstract Task<(IAsyncDisposable Container, string ConnectionString)> StartAsync();

    protected abstract DbConnection CreateAdminConnection();

    protected abstract string FindLockHoldersSql { get; }

    protected abstract string KillSql(object session);

    private static bool IsDockerUnavailable(Exception exception) =>
        exception is DockerUnavailableException ||
        exception.GetType().Name.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is not null && IsDockerUnavailable(exception.InnerException);
}

public sealed class PostgreSqlLockDatabase() : LockDatabase("PostgreSql", "PostgreSQL")
{
    public const string Collection = "locking-database-postgresql";

    protected override string FindLockHoldersSql =>
        "SELECT DISTINCT pid FROM pg_locks WHERE locktype = 'advisory' AND pid <> pg_backend_pid()";

    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    }

    protected override DbConnection CreateAdminConnection() => new NpgsqlConnection(ConnectionString);

    protected override string KillSql(object session) => $"SELECT pg_terminate_backend({session})";
}

public sealed class SqlServerLockDatabase() : LockDatabase("SqlServer", "SQL Server")
{
    public const string Collection = "locking-database-sqlserver";

    protected override string FindLockHoldersSql =>
        "SELECT DISTINCT request_session_id FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION' AND request_session_id <> @@SPID";

    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    }

    protected override DbConnection CreateAdminConnection() => new SqlConnection(ConnectionString);

    protected override string KillSql(object session) => $"KILL {session}";
}

public sealed class MySqlLockDatabase() : LockDatabase("MySql", "MySQL")
{
    public const string Collection = "locking-database-mysql";

    protected override string FindLockHoldersSql =>
        "SELECT DISTINCT t.PROCESSLIST_ID FROM performance_schema.metadata_locks m " +
        "JOIN performance_schema.threads t ON t.THREAD_ID = m.OWNER_THREAD_ID " +
        "WHERE m.OBJECT_TYPE = 'USER LEVEL LOCK' AND t.PROCESSLIST_ID <> CONNECTION_ID()";

    protected override async Task<(IAsyncDisposable, string)> StartAsync()
    {
        var container = new MySqlBuilder("mysql:8.4.11").WithUsername("root").WithPassword("root").Build();
        await container.StartAsync();
        return (container, container.GetConnectionString());
    }

    protected override DbConnection CreateAdminConnection() => new MySqlConnection(ConnectionString);

    protected override string KillSql(object session) => $"KILL {session}";
}

[CollectionDefinition(PostgreSqlLockDatabase.Collection)]
public sealed class PostgreSqlLockCollection : ICollectionFixture<PostgreSqlLockDatabase>;

[CollectionDefinition(SqlServerLockDatabase.Collection)]
public sealed class SqlServerLockCollection : ICollectionFixture<SqlServerLockDatabase>;

[CollectionDefinition(MySqlLockDatabase.Collection)]
public sealed class MySqlLockCollection : ICollectionFixture<MySqlLockDatabase>;
