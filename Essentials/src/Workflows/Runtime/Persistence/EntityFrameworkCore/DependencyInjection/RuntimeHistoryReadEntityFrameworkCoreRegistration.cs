using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

// Read-side selection for the existing public native aggregate registration. Capture/independent
// mutation admission remains separate; an explicitly supplied foreign history reader is preserved.
internal static class RuntimeHistoryReadEntityFrameworkCoreRegistration
{
    private static readonly Func<IServiceProvider, object> Factory = provider => new EfRuntimeHistoryStore(
        provider.GetRequiredService<RuntimeDbContext>(), provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
        provider.GetRequiredService<IRuntimeRecoveryContinuationCodec>(), RuntimeHistoryEfModule.Chain);

    internal static void AddOwnedReader(IServiceCollection services)
    {
        var readers = services.Where(descriptor => descriptor.ServiceType == typeof(IRuntimeHistoryStore)).ToArray();
        if (readers is [{ Lifetime: ServiceLifetime.Scoped } selected] && ReferenceEquals(selected.ImplementationFactory, Factory)) return;
        if (readers.Length > 0 && !(readers is [{ Lifetime: ServiceLifetime.Scoped } owned] && owned.ImplementationType == typeof(InMemoryRuntimeHistoryStore)))
            return;
        foreach (var descriptor in readers) services.Remove(descriptor);
        // All read facets are implemented by this one selected scoped instance. Consumers qualify
        // the actual IRuntimeHistoryStore; no separately selected details/occurrence backend is added.
        services.Add(ServiceDescriptor.Scoped(typeof(IRuntimeHistoryStore), Factory));
    }
}
