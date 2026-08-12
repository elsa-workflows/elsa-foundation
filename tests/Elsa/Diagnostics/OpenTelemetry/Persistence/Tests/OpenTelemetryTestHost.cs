using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests;

internal sealed class OpenTelemetryTestHost : IDbContextFactory<OpenTelemetryDbContext>, IDisposable
{
    private readonly SqliteConnection _rootConnection;
    private readonly string _connectionString;
    private readonly ServiceProvider _services;

    private OpenTelemetryTestHost(SqliteConnection rootConnection, string connectionString, ServiceProvider services)
    {
        _rootConnection = rootConnection;
        _connectionString = connectionString;
        _services = services;
    }

    public OpenTelemetryDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OpenTelemetryDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new OpenTelemetryDbContext(options, _services);
    }

    public Task<OpenTelemetryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    /// <summary>
    /// Pass <paramref name="fileDataSource"/> to bind a real on-disk database instead of the
    /// shared-cache in-memory default. The default cannot outlive <see cref="Dispose"/>, so the #646
    /// differential would otherwise compare a volatile EF store against a file-backed Groundwork one and
    /// report agreement on every restart assertion. The diagnostics workload contract requires the
    /// <c>file-backed-distinct-connections</c> SQLite topology for exactly that reason; that the retained
    /// EF oracle is the SQLite comparand is recorded by the workload's <c>correctness.timingGate</c>,
    /// which is where the gate regime belongs rather than inside a topology identifier.
    /// </summary>
    public static OpenTelemetryTestHost Create(string? fileDataSource = null)
    {
        var connectionString = fileDataSource is null
            ? $"Data Source=otel-{Guid.NewGuid():N};Mode=Memory;Cache=Shared"
            : $"Data Source={fileDataSource}";
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();

        var provider = services.BuildServiceProvider();
        var host = new OpenTelemetryTestHost(connection, connectionString, provider);
        using var ctx = host.CreateDbContext();
        ctx.Database.EnsureCreated();
        return host;
    }

    public void Dispose()
    {
        _services.Dispose();
        _rootConnection.Dispose();
    }
}
