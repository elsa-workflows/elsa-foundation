using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the bounded R19 EF checkpoint slice for explicit preview/test composition.</summary>
public static class RuntimeCheckpointCommitEntityFrameworkCoreRegistration
{
    /// <summary>
    /// Registers the concrete checkpoint adapter only after a Runtime EF context is owned by the composition.
    /// This preparatory R19 slice intentionally does not replace <see cref="IRuntimeCheckpointCommitStore"/>:
    /// R20-R24 state participants and the complete writer remain pending.
    /// </summary>
    public static IServiceCollection AddRuntimeCheckpointCommitEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (RuntimeOperationalStateStoreBackend.Find(services)?.Name != RuntimeOperationalStateStoreBackend.EntityFramework ||
            !services.Any(descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext)))
        {
            throw new InvalidOperationException(
                "Runtime checkpoint EF persistence requires an owned Runtime EF operational-state context. Register Runtime operational-state EF persistence first.");
        }

        services.TryAddScoped<EfRuntimeCheckpointCommitStore>();
        return services;
    }
}
