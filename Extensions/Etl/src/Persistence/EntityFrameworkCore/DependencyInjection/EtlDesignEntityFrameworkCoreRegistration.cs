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
        if (!StringComparer.Ordinal.Equals(provider, "sqlite"))
            throw new NotSupportedException("The initial ETL design EF module supports SQLite only.");

        Binding.AddContext<EtlDesignSqliteDbContext>(services, options.Pooling, (serviceProvider, builder) =>
            Binding.Apply(builder, serviceProvider, provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<EtlDesignDbContext>(providerServices => providerServices.GetRequiredService<EtlDesignSqliteDbContext>());
        services.AddScoped<IEtlDefinitionStore, EfEtlDefinitionStore>();
        return services;
    }
}
