using Elsa.Modularity.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Modularity.EntityFramework.Extensions;

public static class ModularityEntityFrameworkServiceCollectionExtensions
{
    /// <summary>
    /// Composes the EF activation guard into the container that owns feature management — the host's, not
    /// a shell's (ADR 0076 D9). It has to be registered before any EF feature is enabled, which is why an
    /// EF feature cannot bring it: the first one enabled on a shell that has none would find no guard at all.
    /// </summary>
    public static IServiceCollection AddEfPendingMigrationActivationGuard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IEfModuleAssemblySource, LoadedEfModuleAssemblySource>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureActivationGuard, EfPendingMigrationActivationGuard>());
        return services;
    }
}
