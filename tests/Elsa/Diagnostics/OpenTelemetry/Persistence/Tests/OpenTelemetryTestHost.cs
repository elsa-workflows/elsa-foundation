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

    public static OpenTelemetryTestHost Create()
    {
        var connectionString = $"Data Source=otel-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
