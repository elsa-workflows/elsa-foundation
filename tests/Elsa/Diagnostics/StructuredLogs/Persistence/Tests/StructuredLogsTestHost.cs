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
/// A SQLite in-memory host exposing an <see cref="IDbContextFactory{TContext}"/> over a single open
/// connection so the database survives across DbContext instances. Wires the services
/// <c>ElsaDbContextBase</c> resolves at runtime (<see cref="ISystemClock"/> + the SQLite model-creating
/// handler). Pass <c>createSchema: false</c> to simulate the pre-migration window.
/// </summary>
internal sealed class StructuredLogsTestHost : IDbContextFactory<StructuredLogsDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    private StructuredLogsTestHost(SqliteConnection connection, ServiceProvider services)
    {
        _connection = connection;
        _services = services;
    }

    public StructuredLogsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<StructuredLogsDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new StructuredLogsDbContext(options, _services);
    }

    public Task<StructuredLogsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());

    public static StructuredLogsTestHost Create(bool createSchema = true)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();

        var provider = services.BuildServiceProvider();
        var host = new StructuredLogsTestHost(connection, provider);

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
        _connection.Dispose();
    }
}
