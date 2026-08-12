using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests;

/// <summary>
/// A SQLite in-memory host exposing an <see cref="IDbContextFactory{TContext}"/> over a named
/// shared-cache in-memory database. A root connection is held open so the database survives across
/// DbContext instances, but every context gets its own connection — <see cref="SqliteConnection"/> is
/// not thread-safe, and the drain-loop tests probe the database concurrently with the store's writes
/// (same pattern as <c>OpenTelemetryTestHost</c>). Wires the services <c>ElsaDbContextBase</c> resolves
/// at runtime (<see cref="ISystemClock"/> + the SQLite model-creating handler). Pass
/// <c>createSchema: false</c> to simulate the pre-migration window.
/// </summary>
/// <remarks>
/// Pass <c>fileDataSource</c> to bind a real on-disk database instead. The shared-cache in-memory
/// default cannot outlive <see cref="Dispose"/>, so a differential that compares this store against a
/// file-backed provider would compare a volatile store against a durable one and report agreement on
/// every restart assertion. The #646 diagnostics workload requires the
/// <c>file-backed-distinct-connections</c> SQLite topology for exactly that reason; that the retained EF
/// oracle is the SQLite comparand is recorded by the workload's <c>correctness.timingGate</c>, which is
/// where the gate regime belongs rather than inside a topology identifier.
/// </remarks>
internal sealed class StructuredLogsTestHost : IDbContextFactory<StructuredLogsDbContext>, IDisposable
{
    private readonly SqliteConnection _rootConnection;
    private readonly string _connectionString;
    private readonly ServiceProvider _services;

    private StructuredLogsTestHost(SqliteConnection rootConnection, string connectionString, ServiceProvider services)
    {
        _rootConnection = rootConnection;
        _connectionString = connectionString;
        _services = services;
    }

    public StructuredLogsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<StructuredLogsDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new StructuredLogsDbContext(options, _services);
    }

    public Task<StructuredLogsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());

    public static StructuredLogsTestHost Create(bool createSchema = true, string? fileDataSource = null)
    {
        var connectionString = fileDataSource is null
            ? $"Data Source=structured-logs-{Guid.NewGuid():N};Mode=Memory;Cache=Shared"
            : $"Data Source={fileDataSource}";
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();

        var provider = services.BuildServiceProvider();
        var host = new StructuredLogsTestHost(connection, connectionString, provider);

        if (createSchema)
        {
            using var ctx = host.CreateDbContext();
            ctx.Database.EnsureCreated();
        }

        return host;
    }

    public void Dispose()
    {
        _services.Dispose();
        _rootConnection.Dispose();
    }
}
