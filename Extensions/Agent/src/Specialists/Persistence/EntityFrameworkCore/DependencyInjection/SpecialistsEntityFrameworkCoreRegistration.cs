using Elsa.Agent.Specialists.Core.DependencyInjection;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.DependencyInjection;

public static class SpecialistsEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(SpecialistsDbContext));
    public static IServiceCollection AddSpecialistsEntityFrameworkCore(this IServiceCollection services, SpecialistsEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var add = Binding.Select<Action<IServiceCollection, SpecialistsEntityFrameworkCoreOptions>>(options.Provider,
            AddContext<SpecialistsSqliteDbContext>, AddContext<SpecialistsSqlServerDbContext>, AddContext<SpecialistsPostgreSqlDbContext>, AddContext<SpecialistsMySqlDbContext>);
        services.AddSpecialistTurnStore<SpecialistsDbContext>();
        services.AddSingleton(options);
        add(services, options);
        services.AddEfModuleMigrations<SpecialistsDbContext>(options.Provider);
        return services;
    }

    /// <summary>Uses fresh owned write contexts; the caller's tracked work and transaction are not enlisted.</summary>
    public static IServiceCollection AddSpecialistTurnStore<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.ReplaceSpecialistTurnStore(provider => new EfSpecialistTurnStore(provider.GetRequiredService<TContext>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>()));
        services.AddPersistenceCore();
        return services;
    }
    private static void AddContext<TContext>(IServiceCollection services, SpecialistsEntityFrameworkCoreOptions options) where TContext : SpecialistsDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) => Binding.Apply(builder, provider,
            options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<SpecialistsDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class SpecialistsEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? Schema { get; set; }
    public bool Pooling { get; set; }
}
