using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.DependencyInjection;

internal sealed class EtlRuntimeEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? Schema { get; set; }
    public bool Pooling { get; set; }
    public TimeSpan PreparationLease { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan PartitionLease { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan DeliveryLease { get; set; } = TimeSpan.FromSeconds(30);
    public int MaximumPartitionsPerRun { get; set; } = 128;
    public int MaximumPageSize { get; set; } = 100;
}

internal static class EtlRuntimeEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(EtlRuntimeDbContext));

    internal static IServiceCollection AddEtlRuntimeEntityFrameworkCore(
        this IServiceCollection services,
        EtlRuntimeEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        if (!StringComparer.Ordinal.Equals(provider, "sqlite"))
            throw new NotSupportedException("The initial ETL runtime EF module supports SQLite only.");

        Binding.AddContext<EtlRuntimeSqliteDbContext>(services, options.Pooling, (serviceProvider, builder) =>
            Binding.Apply(builder, serviceProvider, provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<EtlRuntimeDbContext>(serviceProvider => serviceProvider.GetRequiredService<EtlRuntimeSqliteDbContext>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(new EtlRuntimeStoreOptions(
            options.PreparationLease, options.PartitionLease, options.DeliveryLease,
            options.MaximumPartitionsPerRun, options.MaximumPageSize));
        services.AddScoped<IEtlRuntimeStore, EfEtlRuntimeStore>();
        return services;
    }

    private static void Validate(EtlRuntimeEntityFrameworkCoreOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PreparationLease, TimeSpan.FromSeconds(5));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PartitionLease, TimeSpan.FromSeconds(5));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.DeliveryLease, TimeSpan.FromSeconds(5));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumPartitionsPerRun);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumPageSize);
    }
}
