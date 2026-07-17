using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Groundwork.Core.Transactions;
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
        services.ClaimWorkflowTestScopeProvider(typeof(GroundworkWorkflowTestScopeStore));
        services.AddPersistenceCore();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGroundworkStorageManifestSource, RuntimeGroundworkStorageManifestSource>());

        // Replace the in-memory defaults registered by the runtime API feature. RemoveAll guarantees
        // the bridge wins regardless of feature composition order.
        services.RemoveAll<IBookmarkStateStore>();
        services.AddScoped<IBookmarkStateStore, GroundworkBookmarkStateStore>();
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddScoped<IWorkflowExecutableStore, GroundworkWorkflowExecutableStore>();
        services.RemoveAll<IExecutableActivityTemplateStore>();
        services.AddScoped<IExecutableActivityTemplateStore, GroundworkExecutableActivityTemplateStore>();
        services.RemoveAll<IWorkflowExecutableSourceReferenceStore>();
        services.AddScoped<IWorkflowExecutableSourceReferenceStore, GroundworkWorkflowExecutableSourceReferenceStore>();
        services.RemoveAll<IActivityExecutionStateStore>();
        services.AddScoped<IActivityExecutionStateStore, GroundworkActivityExecutionStateStore>();
        services.RemoveAll<IActivityExecutionInspectionStore>();
        services.RemoveAll<IActivityExecutionInspectionWriter>();
        services.RemoveAll<GroundworkActivityExecutionInspectionStore>();
        services.AddScoped<GroundworkActivityExecutionInspectionStore>();
        services.AddScoped<IActivityExecutionInspectionStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkActivityExecutionInspectionStore>());
        services.AddScoped<IActivityExecutionInspectionWriter>(serviceProvider => serviceProvider.GetRequiredService<GroundworkActivityExecutionInspectionStore>());
        services.RemoveAll<IActivityExecutionHierarchyStore>();
        services.RemoveAll<IActivityExecutionHierarchyReader>();
        services.RemoveAll<IActivityExecutionHierarchyWriter>();
        services.AddScoped<IActivityExecutionHierarchyStore, GroundworkActivityExecutionHierarchyStore>();
        services.AddScoped<IActivityExecutionHierarchyReader>(serviceProvider => serviceProvider.GetRequiredService<IActivityExecutionHierarchyStore>());
        services.AddScoped<IActivityExecutionHierarchyWriter>(serviceProvider => serviceProvider.GetRequiredService<IActivityExecutionHierarchyStore>());
        services.RemoveAll<IWorkflowExecutionStateStore>();
        services.AddScoped<IWorkflowExecutionStateStore>(serviceProvider => new GroundworkWorkflowExecutionStateStore(
            serviceProvider.GetRequiredService<IDocumentStore>(),
            serviceProvider.GetRequiredService<IGroundworkRuntimeDocumentSerializer>(),
            serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            serviceProvider.GetService<IGroundworkWorkflowExecutionStatePageQuery>(),
            serviceProvider.GetService<IBoundedDocumentStore>()
            ?? serviceProvider.GetRequiredService<IDocumentStore>() as IBoundedDocumentStore
            ?? throw new InvalidOperationException("Workflow-execution queries require an admitted bounded document-store runtime.")));
        services.RemoveAll<IWorkflowTestScopeStore>();
        services.RemoveAll<IWorkflowTestScopeAdmissionStore>();
        services.RemoveAll<IWorkflowTestScopeCleanupStore>();
        services.RemoveAll<GroundworkWorkflowTestScopeStore>();
        services.AddScoped<GroundworkWorkflowTestScopeStore>();
        services.AddScoped<IWorkflowTestScopeStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkWorkflowTestScopeStore>());
        services.AddScoped<IWorkflowTestScopeAdmissionStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkWorkflowTestScopeStore>());
        services.RemoveAll<IDurableValueStateStore>();
        services.AddScoped<IDurableValueStateStore, GroundworkDurableValueStateStore>();
        services.RemoveAll<ISchedulerStateStore>();
        services.AddScoped<ISchedulerStateStore, GroundworkSchedulerStateStore>();
        services.RemoveAll<IExecutionLivenessStateStore>();
        services.AddScoped<IExecutionLivenessStateStore, GroundworkExecutionLivenessStateStore>();
        services.RemoveAll<IWorkflowHoldStateStore>();
        services.AddScoped<IWorkflowHoldStateStore, GroundworkWorkflowHoldStateStore>();
        services.RemoveAll<IIncidentStateStore>();
        services.AddScoped<IIncidentStateStore, GroundworkIncidentStateStore>();
        services.RemoveAll<IWorkflowDispatchStore>();
        services.RemoveAll<IWorkflowDispatchQueryStore>();
        services.RemoveAll<IWorkflowDispatchDeleteStore>();
        services.RemoveAll<IWorkflowDispatchRetentionRootStore>();
        services.RemoveAll<IWorkflowDispatchAdmissionStore>();
        services.RemoveAll<IWorkflowDispatchCancellationStore>();
        services.RemoveAll<GroundworkWorkflowDispatchStore>();
        services.AddScoped<GroundworkWorkflowDispatchStore>();
        services.AddScoped<IWorkflowDispatchStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkWorkflowDispatchStore>());
        services.AddScoped<IWorkflowDispatchQueryStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkWorkflowDispatchStore>());
        services.AddScoped<IWorkflowDispatchDeleteStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkWorkflowDispatchStore>());
        services.AddScoped<IWorkflowDispatchRetentionRootStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkWorkflowDispatchStore>());
        services.AddScoped<IWorkflowDispatchAdmissionStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkWorkflowDispatchStore>());
        services.AddScoped<IWorkflowDispatchCancellationStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkWorkflowDispatchStore>());

        // Durable checkpoint writer. It orchestrates the Groundwork-backed seam stores above and records a
        // restart-safe per-CommitId marker, replacing the in-memory writer registered by the runtime feature.
        services.RemoveAll<IRuntimeCheckpointCommitStore>();
        services.AddScoped<IRuntimeCheckpointCommitStore, GroundworkRuntimeCheckpointWriter>();

        services.RemoveAll<IRuntimePostCommitOutboxStore>();
        services.RemoveAll<IPostCommitOutboxLookupStore>();
        services.RemoveAll<IRuntimePostCommitOutboxClaimStore>();
        services.RemoveAll<IRuntimePostCommitOutboxClaimCompletionStore>();
        services.RemoveAll<IWorkflowDispatchRedriveStore>();
        services.RemoveAll<GroundworkRuntimePostCommitOutboxStore>();
        services.AddScoped<GroundworkRuntimePostCommitOutboxStore>();
        services.AddScoped<IRuntimePostCommitOutboxStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkRuntimePostCommitOutboxStore>());
        services.AddScoped<IPostCommitOutboxLookupStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkRuntimePostCommitOutboxStore>());
        services.AddScoped<IRuntimePostCommitOutboxClaimStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkRuntimePostCommitOutboxStore>());
        services.AddScoped<IRuntimePostCommitOutboxClaimCompletionStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkRuntimePostCommitOutboxStore>());
        services.AddScoped<IWorkflowDispatchRedriveStore>(serviceProvider => serviceProvider.GetRequiredService<GroundworkRuntimePostCommitOutboxStore>());
        services.AddScoped<IWorkflowTestScopeCleanupStore, GroundworkTestScopeCleanupStore>();

        // Versioned document serialization: every bridge store routes its content JSON through the
        // serializer, which stamps per-kind schema versions on write and enforces each current-only pre-GA
        // boundary on read. TryAdd keeps host-supplied serializer replacements intact.
        services.TryAddSingleton<IGroundworkRuntimeDocumentSerializer, GroundworkRuntimeDocumentSerializer>();
        // Durable scheduler work queue. Without this swap the post-commit outbox delivers into the
        // process-local in-memory queue, and a crash after checkpoint commit loses the continuation
        // even though state and outbox items were stored durably.
        services.RemoveAll<IWorkflowSchedulerWorkQueue>();
        services.AddScoped<IWorkflowSchedulerWorkQueue, GroundworkWorkflowSchedulerWorkQueue>();

        // Durable scheduler poison store. Without this swap handler crashes recorded by the drainer live only
        // in process memory and disappear on restart.
        services.RemoveAll<IWorkflowSchedulerPoisonStore>();
        services.AddScoped<IWorkflowSchedulerPoisonStore, GroundworkWorkflowSchedulerPoisonStore>();

        // Durable timer store. Without this swap timers live only in the process-local in-memory store and
        // are lost on restart, so a Delay would never resume after a crash.
        services.RemoveAll<IDurableTimerStore>();
        services.AddScoped<IDurableTimerStore, GroundworkDurableTimerStore>();

        // Readiness evidence is contributed per durability boundary. Distinct implementation types are
        // intentional: TryAddEnumerable de-duplicates by implementation type, and the readiness assessor
        // must observe all four Groundwork-backed boundaries independently.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkCheckpointDurabilityEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkDispatchStoreDurabilityEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkOutboxDurabilityEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkSchedulerDurabilityEvidence>());

        // Durable trigger index (W7, E3-1). Without this swap the trigger bindings written at publish time
        // live only in the process-local in-memory store, so a restart loses the ability to start workflows
        // from a stimulus even though the published executable is durable.
        services.RemoveAll<IWorkflowTriggerBindingStore>();
        services.AddScoped<IWorkflowTriggerBindingStore, GroundworkWorkflowTriggerBindingStore>();

        // Durable recurring-trigger schedule store (W16). Without this swap the Timer/Cron schedules written at
        // publish time live only in the process-local in-memory store, so a restart forgets every recurring
        // start trigger until the workflow is republished.
        services.RemoveAll<IRecurringTriggerScheduleStore>();
        services.AddScoped<IRecurringTriggerScheduleStore, GroundworkRecurringTriggerScheduleStore>();

        return services;
    }
}

internal sealed class GroundworkCheckpointDurabilityEvidence(GroundworkStoreSessionSource? sessionSource = null) : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Checkpoint;
    public WorkflowDispatchDurabilityLevel Level => sessionSource?.AdmittedTransactionBoundary == TransactionBoundary.CrossUnitAtomic
        ? WorkflowDispatchDurabilityLevel.Durable
        : WorkflowDispatchDurabilityLevel.ProcessLocal;
}

internal sealed class GroundworkDispatchStoreDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.DispatchStore;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}

internal sealed class GroundworkOutboxDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Outbox;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}

internal sealed class GroundworkSchedulerDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Scheduler;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}
