using Npgsql;
using Testcontainers.PostgreSql;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>
/// One PostgreSQL server for every measurement in the run, with each measured host on an empty database of its own,
/// dropped after the run, and
/// its connections routed through <see cref="PostgreSqlWireCounter"/>. Starts on the first database a measurement asks for.
/// Set <c>ELSA_COMMAND_COST_POSTGRESQL_CONNECTION_STRING</c> to measure against an existing server instead of a container.
/// </summary>
public sealed class CommandCostPostgreSqlServer : IAsyncLifetime
{
    private const string ConnectionStringVariable = "ELSA_COMMAND_COST_POSTGRESQL_CONNECTION_STRING";
    private const string DatabasePrefix = "command_cost_";

    private PostgreSqlContainer? _container;
    private string? _adminConnectionString;
    private PostgreSqlWireCounter? _wire;
    private readonly SemaphoreSlim _starting = new(1, 1);

    internal PostgreSqlWireCounter Wire => _wire ?? throw new InvalidOperationException("The PostgreSQL server was not started.");

    public Task InitializeAsync() => Task.CompletedTask;

    private async Task EnsureStartedAsync()
    {
        await _starting.WaitAsync();
        try
        {
            if (_wire is not null)
                return;

            _adminConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
            if (string.IsNullOrWhiteSpace(_adminConnectionString))
            {
                _container = new PostgreSqlBuilder("postgres:16-alpine")
                    .WithDatabase("elsa_command_cost")
                    .WithUsername("postgres")
                    .WithPassword("postgres")
                    .Build();
                await _container.StartAsync();
                _adminConnectionString = _container.GetConnectionString();
            }

            var admin = new NpgsqlConnectionStringBuilder(_adminConnectionString);
            _wire = new PostgreSqlWireCounter(admin.Host!, admin.Port);
        }
        finally
        {
            _starting.Release();
        }
    }

    /// <summary>Creates an empty database, reached through the wire counter without TLS. Dispose it when the run is over.</summary>
    internal async Task<PostgreSqlRunDatabase> CreateDatabaseAsync()
    {
        await EnsureStartedAsync();
        var database = $"{DatabasePrefix}{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Host = "127.0.0.1",
            Port = Wire.Port,
            Database = database,
            SslMode = SslMode.Disable
        }.ConnectionString;
        return new PostgreSqlRunDatabase(database, connectionString, _adminConnectionString!);
    }

    /// <summary>The per-run databases still on the server: none once every run has disposed its database.</summary>
    internal async Task<int> CountRunDatabasesAsync()
    {
        if (_adminConnectionString is null)
            return 0;
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM pg_database WHERE starts_with(datname, '{DatabasePrefix}')", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (_wire is not null)
            await _wire.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
        _starting.Dispose();
    }
}

/// <summary>
/// One measured run's database. Disposing it closes the run's pooled connections and then drops the database, so a
/// measurement against an existing server, through <c>ELSA_COMMAND_COST_POSTGRESQL_CONNECTION_STRING</c>, leaves
/// neither databases nor idle connections behind. The drop does not force: if a pooled connection survived, it fails.
/// </summary>
internal sealed class PostgreSqlRunDatabase(string name, string connectionString, string adminConnectionString) : IAsyncDisposable
{
    public string ConnectionString => connectionString;

    public async ValueTask DisposeAsync()
    {
        await using (var pooled = new NpgsqlConnection(connectionString))
            NpgsqlConnection.ClearPool(pooled);

        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE {name}", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
