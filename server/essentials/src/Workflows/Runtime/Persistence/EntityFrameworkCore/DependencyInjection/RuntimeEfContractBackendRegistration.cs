using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

internal static class RuntimeEfContractBackendRegistration
{
    public static void EnsureSharedContext(IServiceCollection services, string owner)
    {
        var operational = RuntimeOperationalStateStoreBackend.Find(services);
        if (operational?.Name != RuntimeOperationalStateStoreBackend.EntityFramework)
            throw new InvalidOperationException(
                $"{owner} requires an EF runtime operational-state backend. Register Runtime operational-state EF persistence first.");

        operational.EnsureOwnsRegisteredContracts(services);

        var contextDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(RuntimeDbContext))
            .ToArray();
        if (contextDescriptors.Length != 1)
            throw new InvalidOperationException(
                $"{owner} requires exactly one shared RuntimeDbContext registration owned by Runtime EF persistence.");

        var context = contextDescriptors[0];
        var ownsContext =
            operational.Owns(context) ||
            RuntimeArtifactStoreBackend.Find(services)?.Owns(context) == true ||
            RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(context) == true ||
            BookmarkStateStoreBackend.Find(services)?.Owns(context) == true ||
            WorkflowExecutionStateStoreBackend.Find(services)?.Owns(context) == true ||
            RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(context) == true ||
            WorkflowTestScopeStoreBackend.Find(services)?.Owns(context) == true ||
            SchedulerWorkQueueStoreBackend.Find(services)?.Owns(context) == true ||
            DurableTimerStoreBackend.Find(services)?.Owns(context) == true ||
            WorkflowSchedulerPoisonStoreBackend.Find(services)?.Owns(context) == true;

        if (!ownsContext)
            throw new InvalidOperationException(
                $"{owner} refuses to reuse an unowned RuntimeDbContext registration.");
    }
}
