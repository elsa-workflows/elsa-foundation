using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Services;
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
internal sealed class ActivitiesDesignTestHost : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    private ActivitiesDesignTestHost(SqliteConnection connection, ServiceProvider services, IImplementationDescriptorRegistry descriptorRegistry)
    {
        _connection = connection;
        _services = services;
        DescriptorRegistry = descriptorRegistry;
    }

    public IServiceProvider Services => _services;

    /// <summary>
    /// Exposed for tests that exercise the loading handler — the loading handler
    /// uses the registry to look up the descriptor type before deserializing.
    /// </summary>
    public IImplementationDescriptorRegistry DescriptorRegistry { get; }

    public ActivitiesDesignDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ActivitiesDesignDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ActivitiesDesignDbContext(options, _services);
    }

    public static ActivitiesDesignTestHost Create() => CreateWithDescriptorRegistry();

    /// <summary>
    /// Construct a host pre-populated with the given (kind, descriptor-type) registrations
    /// so the loading handler can resolve descriptor JSON back into a concrete type.
    /// </summary>
    public static ActivitiesDesignTestHost CreateWithDescriptorRegistry(params (string kind, Type type)[] registrations)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var registry = new ImplementationDescriptorRegistry();
        foreach (var (kind, type) in registrations)
            registry.Register(new ImplementationDescriptorRegistration(kind, type));

        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
        services.AddSingleton<IImplementationDescriptorRegistry>(registry);

        var provider = services.BuildServiceProvider();
        var host = new ActivitiesDesignTestHost(connection, provider, registry);

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
