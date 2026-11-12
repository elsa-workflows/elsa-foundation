using Elsa.Persistence.EntityFramework;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;

public static class DashboardResourcePersistenceRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(DashboardResourceDbContext));

    public static IServiceCollection AddDashboardResourcesEntityFrameworkCore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IDashboardResourceStore)))
            throw new InvalidOperationException("Exactly one Dashboard resource store must be configured.");
        services.AddScoped<IDashboardResourceStore, EfDashboardResourceStore<TContext>>();
        return services;
    }

    public static IServiceCollection AddDashboardResourcesEntityFrameworkCore(this IServiceCollection services,
        DashboardResourcePersistenceOptions options)
    {
        var add = Binding.Select<Action<IServiceCollection, DashboardResourcePersistenceOptions>>(options.Provider,
            Add<DashboardResourcesSqliteDbContext>, Add<DashboardResourcesSqlServerDbContext>,
            Add<DashboardResourcesPostgreSqlDbContext>, Add<DashboardResourcesMySqlDbContext>);
        add(services, options);
        return services;
    }

    private static void Add<TContext>(IServiceCollection services, DashboardResourcePersistenceOptions options) where TContext : DashboardResourceDbContext
    {
        Binding.AddContext<TContext>(services, false, (provider, builder) => Binding.Apply(builder, provider,
            options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<DashboardResourceDbContext>(provider => provider.GetRequiredService<TContext>());
        services.AddDashboardResourcesEntityFrameworkCore<TContext>();
    }
}

public sealed class DashboardResourcePersistenceOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? Schema { get; set; }
}
