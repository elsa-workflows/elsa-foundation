using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.DependencyInjection;

internal sealed class EtlDesignEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? Schema { get; set; }
    public bool Pooling { get; set; }
}

internal static class EtlDesignEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(EtlDesignDbContext));

    internal static IServiceCollection AddEtlDesignEntityFrameworkCore(
        this IServiceCollection services,
        EtlDesignEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var addContext = Binding.Select<Action<IServiceCollection, EtlDesignEntityFrameworkCoreOptions, string>>(
            provider,
            AddContext<EtlDesignSqliteDbContext>,
            AddContext<EtlDesignSqlServerDbContext>,
            AddContext<EtlDesignPostgreSqlDbContext>,
            AddContext<EtlDesignMySqlDbContext>);
        addContext(services, options, provider);
        services.AddScoped<EfEtlDefinitionStore>();
        services.AddScoped<IEtlDefinitionStore>(providerServices => providerServices.GetRequiredService<EfEtlDefinitionStore>());
        services.AddScoped<IEtlDefinitionStoreV2>(providerServices => providerServices.GetRequiredService<EfEtlDefinitionStore>());
        services.AddScoped<IEtlDefinitionCatalog, EfEtlDefinitionCatalog>();
        return services;
    }

    private static void AddContext<TContext>(IServiceCollection services, EtlDesignEntityFrameworkCoreOptions options, string provider)
        where TContext : EtlDesignDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (serviceProvider, builder) =>
            Binding.Apply(builder, serviceProvider, provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<EtlDesignDbContext>(providerServices => providerServices.GetRequiredService<TContext>());
    }
}
