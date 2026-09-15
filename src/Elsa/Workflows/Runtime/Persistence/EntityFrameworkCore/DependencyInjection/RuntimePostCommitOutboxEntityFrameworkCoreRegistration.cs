using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the R20 EF post-commit outbox adapter for explicit preview/test composition.</summary>
public static class RuntimePostCommitOutboxEntityFrameworkCoreRegistration
{
    /// <summary>
    /// Registers the concrete R20 adapter only after Runtime EF owns the shared context. The runtime outbox
    /// contracts remain untouched until R21 dispatch projection and redrive can be composed atomically.
    /// </summary>
    public static IServiceCollection AddRuntimePostCommitOutboxEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (RuntimeOperationalStateStoreBackend.Find(services)?.Name != RuntimeOperationalStateStoreBackend.EntityFramework ||
            !services.Any(descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext)))
        {
            throw new InvalidOperationException(
                "Runtime post-commit outbox EF persistence requires an owned Runtime EF operational-state context. Register Runtime operational-state EF persistence first.");
        }

        services.TryAddScoped<EfRuntimePostCommitOutboxStore>();
        return services;
    }
}
