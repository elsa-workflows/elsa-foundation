using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the Groundwork-backed runtime store bridges. A provider feature is responsible for
/// registering the concrete <see cref="Groundwork.Documents.Store.IDocumentStore"/> these bridges
/// consume; this method only swaps the runtime store contracts over to the bridge implementations.
/// </summary>
public static class GroundworkRuntimeStoreRegistration
{
    public static IServiceCollection AddGroundworkRuntimeStores(this IServiceCollection services)
    {
        // Replace the in-memory defaults registered by the runtime API feature. RemoveAll guarantees
        // the bridge wins regardless of feature composition order.
        services.RemoveAll<IBookmarkStateStore>();
        services.AddSingleton<IBookmarkStateStore, GroundworkBookmarkStateStore>();
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddSingleton<IWorkflowExecutableStore, GroundworkWorkflowExecutableStore>();
        services.RemoveAll<IActivityExecutionStateStore>();
        services.AddSingleton<IActivityExecutionStateStore, GroundworkActivityExecutionStateStore>();
        services.RemoveAll<IActivityExecutionInspectionStore>();
        services.RemoveAll<IActivityExecutionInspectionWriter>();
        services.RemoveAll<GroundworkActivityExecutionInspectionStore>();
        services.AddSingleton<GroundworkActivityExecutionInspectionStore>();
        services.AddSingleton<IActivityExecutionInspectionStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkActivityExecutionInspectionStore>());
        services.AddSingleton<IActivityExecutionInspectionWriter>(serviceProvider => serviceProvider.GetRequiredService<GroundworkActivityExecutionInspectionStore>());
        services.RemoveAll<IWorkflowExecutionStateStore>();
        services.AddSingleton<IWorkflowExecutionStateStore, GroundworkWorkflowExecutionStateStore>();
        services.RemoveAll<IDurableValueStateStore>();
        services.AddSingleton<IDurableValueStateStore, GroundworkDurableValueStateStore>();
        services.RemoveAll<ISchedulerStateStore>();
        services.AddSingleton<ISchedulerStateStore, GroundworkSchedulerStateStore>();
        services.RemoveAll<IOperationalStateStore>();
        services.AddSingleton<IOperationalStateStore, GroundworkOperationalStateStore>();
        services.RemoveAll<IControlPlaneStateStore>();
        services.AddSingleton<IControlPlaneStateStore, GroundworkControlPlaneStateStore>();
        services.RemoveAll<IIncidentStateStore>();
        services.AddSingleton<IIncidentStateStore, GroundworkIncidentStateStore>();

        // Durable checkpoint writer. It orchestrates the Groundwork-backed seam stores above and records a
        // restart-safe per-CommitId marker, replacing the in-memory writer registered by the runtime feature.
        services.RemoveAll<IRuntimeCheckpointWriter>();
        services.AddSingleton<IRuntimeCheckpointWriter, GroundworkRuntimeCheckpointWriter>();

        services.RemoveAll<IRuntimePostCommitOutboxStore>();
        services.AddSingleton<IRuntimePostCommitOutboxStore, GroundworkRuntimePostCommitOutboxStore>();

        return services;
    }
}
