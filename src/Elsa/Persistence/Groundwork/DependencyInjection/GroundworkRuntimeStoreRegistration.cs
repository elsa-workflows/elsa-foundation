using Elsa.Persistence.Groundwork.Serialization;
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
        services.RemoveAll<IRuntimeCheckpointCommitStore>();
        services.AddSingleton<IRuntimeCheckpointCommitStore, GroundworkRuntimeCheckpointWriter>();

        services.RemoveAll<IRuntimePostCommitOutboxStore>();
        services.AddSingleton<IRuntimePostCommitOutboxStore, GroundworkRuntimePostCommitOutboxStore>();

        // Versioned document serialization: every bridge store routes its content JSON through the
        // serializer, which stamps per-kind schema versions on write and enforces them (with upcasting)
        // on read. TryAdd keeps host-supplied replacements and contributed upcasters intact.
        services.TryAddSingleton<IGroundworkRuntimeDocumentSerializer, GroundworkRuntimeDocumentSerializer>();
        services.TryAddSingleton<IGroundworkRuntimeDocumentUpcasterRegistry, GroundworkRuntimeDocumentUpcasterRegistry>();

        // Durable scheduler work queue. Without this swap the post-commit outbox delivers into the
        // process-local in-memory queue, and a crash after checkpoint commit loses the continuation
        // even though state and outbox items were stored durably.
        services.RemoveAll<IWorkflowSchedulerWorkQueue>();
        services.AddSingleton<IWorkflowSchedulerWorkQueue, GroundworkWorkflowSchedulerWorkQueue>();

        return services;
    }
}
