using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core Dashboard run-health reader.</summary>
public static class WorkflowRunHealthEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddWorkflowRunHealthEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (RuntimeOperationalStateStoreBackend.Find(services)?.Name != RuntimeOperationalStateStoreBackend.EntityFramework ||
            !services.Any(descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext)))
            throw new InvalidOperationException(
                "Dashboard run-health EF persistence requires an owned Runtime EF operational-state context. Register Runtime operational-state EF persistence first.");

        services.RemoveAll<IWorkflowRunHealthDataSource>();
        services.AddScoped<EfWorkflowRunHealthDataSource>();
        services.AddScoped<IWorkflowRunHealthDataSource>(provider =>
            provider.GetRequiredService<EfWorkflowRunHealthDataSource>());
        return services;
    }
}
