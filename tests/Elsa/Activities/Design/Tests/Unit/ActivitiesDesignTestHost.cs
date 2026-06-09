using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Minimal in-memory host for the activities-design DbContext. Wires the DI services that
/// <c>ElsaDbContextBase</c> resolves at runtime (<see cref="ISystemClock"/>, the SQLite
/// model-creating handler) and a SQLite in-memory connection. The connection is kept open
/// for the host's lifetime so the in-memory database survives across DbContext instances.
/// </summary>
/// <remarks>
/// The design domain no longer resolves descriptor types: the loading handler parses the opaque
/// descriptor payload into a <c>JsonElement</c> without any kind→type registry.
/// </remarks>
internal sealed class ActivitiesDesignTestHost : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    private ActivitiesDesignTestHost(SqliteConnection connection, ServiceProvider services)
    {
        _connection = connection;
        _services = services;
    }

    public IServiceProvider Services => _services;

    public ActivitiesDesignDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ActivitiesDesignDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ActivitiesDesignDbContext(options, _services);
    }

    public static ActivitiesDesignTestHost Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();

        var provider = services.BuildServiceProvider();
        var host = new ActivitiesDesignTestHost(connection, provider);

        using var ctx = host.CreateContext();
        ctx.Database.EnsureCreated();

        return host;
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }
}
