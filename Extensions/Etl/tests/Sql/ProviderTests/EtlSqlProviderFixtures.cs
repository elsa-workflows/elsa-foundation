using System.Data.Common;
using DotNet.Testcontainers.Builders;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Sql.PostgreSql;
using Elsa.Etl.Sql.SqlServer;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Elsa.Etl.Sql.ProviderTests;

/// <summary>
/// A real database engine for the ETL SQL delivery suite (spec 1197). A supplied connection string wins; otherwise a
/// container starts. Without Docker the suite skips, unless <see cref="RequireVariable"/> makes it fail, as CI does.
/// </summary>
public abstract class EtlSqlProviderFixture : IAsyncLifetime
{
    internal const string RequireVariable = "ELSA_ETL_SQL_REQUIRE_NATIVE_PROVIDERS";
    private IAsyncDisposable? _container;

    protected abstract string ConnectionStringVariable { get; }
    internal abstract IEtlSqlEngine Engine { get; }
    protected abstract Task<(IAsyncDisposable Container, string ConnectionString)> StartAsync();
    internal abstract DbConnection Open(string connectionString);

    internal string ConnectionString { get; private set; } = string.Empty;
    internal string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } supplied)
        {
            ConnectionString = supplied;
            return;
        }
        try
        {
            (_container, ConnectionString) = await StartAsync();
        }
        catch (Exception exception) when (exception is DockerUnavailableException or HttpRequestException or TimeoutException ||
                                          exception.GetType().Name.Contains("Docker", StringComparison.Ordinal))
        {
            if (Environment.GetEnvironmentVariable(RequireVariable) is "1" or "true")
                throw;
            SkipReason = $"Docker is unavailable for the ETL SQL provider suite ({Engine.Provider}).";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    /// <summary>Runs administration SQL outside the writer.</summary>
    internal async Task ExecuteAsync(string sql)
    {
        await using var connection = Open(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<string[]> QueryAsync(string sql)
    {
        await using var connection = Open(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? ""
                : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture))));
        return rows.ToArray();
    }
}

public sealed class EtlSqlPostgreSqlFixture : EtlSqlProviderFixture
{
    protected override string ConnectionStringVariable => "ELSA_ETL_SQL_POSTGRESQL_TEST_CONNECTION_STRING";
    internal override IEtlSqlEngine Engine { get; } = new PostgreSqlEtlSqlEngine();

    protected override async Task<(IAsyncDisposable Container, string ConnectionString)> StartAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("elsa_etl").WithUsername("postgres").WithPassword("postgres").Build();
        await container.StartAsync();
        return (container, new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Pooling = false }.ConnectionString);
    }

    internal override DbConnection Open(string connectionString) => new NpgsqlConnection(connectionString);
}

public sealed class EtlSqlServerFixture : EtlSqlProviderFixture
{
    protected override string ConnectionStringVariable => "ELSA_ETL_SQL_SQLSERVER_TEST_CONNECTION_STRING";
    internal override IEtlSqlEngine Engine { get; } = new SqlServerEtlSqlEngine();

    protected override async Task<(IAsyncDisposable Container, string ConnectionString)> StartAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();
        return (container, new SqlConnectionStringBuilder(container.GetConnectionString()) { Pooling = false }.ConnectionString);
    }

    internal override DbConnection Open(string connectionString) => new SqlConnection(connectionString);
}
