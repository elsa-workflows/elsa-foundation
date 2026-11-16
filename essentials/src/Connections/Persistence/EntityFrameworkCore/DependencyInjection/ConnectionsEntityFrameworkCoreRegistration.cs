using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.DependencyInjection;

public static class ConnectionsEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(ConnectionsDbContext));

    public static IServiceCollection AddConnectionsEntityFrameworkCore(this IServiceCollection services, ConnectionsEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var add = Binding.Select<Action<IServiceCollection, ConnectionsEntityFrameworkCoreOptions>>(options.Provider,
            AddContext<ConnectionsSqliteDbContext>, AddContext<ConnectionsSqlServerDbContext>, AddContext<ConnectionsPostgreSqlDbContext>, AddContext<ConnectionsMySqlDbContext>);
        services.AddSingleton(options);
        add(services, options);
        services.AddConnectionsRepository<ConnectionsDbContext>();
        services.AddEfModuleMigrations<ConnectionsDbContext>(options.Provider);
        return services;
    }

    /// <summary>
    /// Installs Connections over a host's registered context. The host installs
    /// <see cref="ConnectionsModelBuilderExtensions.AddConnectionsModel"/> and owns its migrations.
    /// Repository writes use fresh instances from the context's options and commit independently;
    /// they do not flush or enlist the caller's tracked changes or transaction.
    /// </summary>
    public static IServiceCollection AddConnectionsRepository<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<IConnectionRepository>();
        services.AddScoped<IConnectionRepository>(provider => new EfConnectionRepository(provider.GetRequiredService<TContext>()));
        return services;
    }

    private static void AddContext<TContext>(IServiceCollection services, ConnectionsEntityFrameworkCoreOptions options) where TContext : ConnectionsDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) =>
            Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<ConnectionsDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class ConnectionsEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? Schema { get; set; }
    public bool Pooling { get; set; }
}
