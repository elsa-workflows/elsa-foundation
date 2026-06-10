using Elsa.Persistence.Groundwork.Diagnostics;
using Elsa.Persistence.Groundwork.Options;
using Elsa.Persistence.Groundwork.Tasks;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddElsaGroundworkPersistence(this IServiceCollection services, Action<GroundworkPersistenceOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<GroundworkPersistenceOptions>();

        services.TryAddSingleton<GroundworkPersistenceDiagnosticStore>();
        services.TryAddScoped<GroundworkPersistenceDiagnostics>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, MaterializeGroundworkStartupTask>());
        return services;
    }
}
