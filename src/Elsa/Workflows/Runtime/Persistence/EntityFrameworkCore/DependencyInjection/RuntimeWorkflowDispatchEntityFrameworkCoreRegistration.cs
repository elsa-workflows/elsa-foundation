using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the R21 dispatch adapter for explicit EF preview composition.</summary>
public static class RuntimeWorkflowDispatchEntityFrameworkCoreRegistration
{
    /// <summary>
    /// Registers the concrete adapter only after an EF operational-state context owns the shared model. Public
    /// dispatch contracts remain untouched until test-scope admission and complete checkpoint composition are wired.
    /// </summary>
    public static IServiceCollection AddRuntimeWorkflowDispatchEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (RuntimeOperationalStateStoreBackend.Find(services)?.Name != RuntimeOperationalStateStoreBackend.EntityFramework ||
            !services.Any(descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext)))
        {
            throw new InvalidOperationException(
                "Runtime workflow-dispatch EF persistence requires an owned Runtime EF operational-state context. Register Runtime operational-state EF persistence first.");
        }

        services.TryAddScoped<EfWorkflowDispatchStore>();
        return services;
    }
}
