using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

// Composition only: installs actual independent ports alongside the owned selected reader.
// Native capture capability and the public capture feature remain separately qualified gates.
internal static class RuntimeHistoryMutationEntityFrameworkCoreRegistration
{
    internal static void AddOwnedMutations(IServiceCollection services)
    {
        var readers = services.Where(descriptor => descriptor.ServiceType == typeof(IRuntimeHistoryStore)).ToArray();
        if (readers.Length != 1 || !RuntimeHistoryReadEntityFrameworkCoreRegistration.IsOwnedReader(readers[0])) return;
        services.TryAddScoped(provider => new EfRuntimeHistoryMutationStore(provider.GetRequiredService<RuntimeDbContext>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            provider.GetRequiredService<IRuntimeHistoryStore>() as EfRuntimeHistoryStore ??
                throw new InvalidOperationException("The independent EF history owner requires its actual selected EF reader."),
            provider.GetRequiredService<TimeProvider>(), RuntimeHistoryEfModule.Chain));

        var observers = services.Where(descriptor => descriptor.ServiceType == typeof(IRuntimeHistoryObservationWriter)).ToArray();
        if (observers.Length == 0 || observers.Length == 1 && RuntimeHistoryRegistration.IsOwnedObservationRegistration(observers[0]))
        {
            foreach (var descriptor in observers) services.Remove(descriptor);
            services.AddScoped<IRuntimeHistoryObservationWriter>(provider => provider.GetRequiredService<EfRuntimeHistoryMutationStore>());
        }
        services.TryAddScoped<IRuntimeHistoryRetentionStore>(provider => provider.GetRequiredService<EfRuntimeHistoryMutationStore>());
    }
}
