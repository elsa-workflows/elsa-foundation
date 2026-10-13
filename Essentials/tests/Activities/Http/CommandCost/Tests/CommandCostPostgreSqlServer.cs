using Npgsql;
using Testcontainers.PostgreSql;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>
/// One PostgreSQL server for every measurement in the run, with each measured host on an empty database of its own and
/// its connections routed through <see cref="PostgreSqlWireCounter"/>. Starts on the first database a measurement asks for.
/// Set <c>ELSA_COMMAND_COST_POSTGRESQL_CONNECTION_STRING</c> to measure against an existing server instead of a container.
/// </summary>
public sealed class CommandCostPostgreSqlServer : IAsyncLifetime
{
    private const string ConnectionStringVariable = "ELSA_COMMAND_COST_POSTGRESQL_CONNECTION_STRING";

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

    /// <summary>Creates an empty database and returns a connection string to it that goes through the wire counter, without TLS.</summary>
    internal async Task<string> CreateDatabaseAsync()
    {
        await EnsureStartedAsync();
        var database = $"command_cost_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Host = "127.0.0.1",
            Port = Wire.Port,
            Database = database,
            SslMode = SslMode.Disable
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_wire is not null)
            await _wire.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
        _starting.Dispose();
    }
}
