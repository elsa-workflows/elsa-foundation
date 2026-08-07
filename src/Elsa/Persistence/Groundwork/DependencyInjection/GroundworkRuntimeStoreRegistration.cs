using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
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
    /// <summary>Key used for the durable Groundwork executable-store backend behind optional decorators.</summary>
    public const string WorkflowExecutableProviderKey = "Elsa.Persistence.Groundwork.WorkflowExecutableProvider";

    /// <summary>Registers Groundwork runtime stores with the bounded executable cache enabled by default.</summary>
    /// <remarks>
    /// There is deliberately no <c>(services, targetName)</c> overload: <c>null</c> would be ambiguous
    /// between it and the cache-options overload. A host naming a target passes options explicitly.
    /// </remarks>
    public static IServiceCollection AddGroundworkRuntimeStores(this IServiceCollection services) =>
        services.AddGroundworkRuntimeStores(new WorkflowExecutableCacheOptions());

    /// <summary>Registers Groundwork runtime stores with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkRuntimeStores(
        this IServiceCollection services,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        string? targetName = null)
    {
        var lane = services.GroundworkLane(targetName);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);

        var cacheOptions = new WorkflowExecutableCacheOptions
        {
            Enabled = workflowExecutableCacheOptions.Enabled,
            Capacity = workflowExecutableCacheOptions.Capacity
        };
        cacheOptions.Validate();

        services.ClaimWorkflowTestScopeProvider(typeof(GroundworkWorkflowTestScopeStore));
        services.AddPersistenceCore();
        lane.Manifest<RuntimeGroundworkStorageManifestSource>();

        // Replace the in-memory defaults registered by the runtime API feature. RemoveAll guarantees
        // the bridge wins regardless of feature composition order.
        lane.Replace<IBookmarkStateStore, GroundworkBookmarkStateStore>();
        services.RemoveAll<IWorkflowExecutableStore>();
        services.RemoveAll<CachingWorkflowExecutableStore>();
        services.RemoveAll<InvalidatingWorkflowExecutableStore>();
        services.RemoveAll<WorkflowExecutableCacheOptions>();
        services.AddSingleton(cacheOptions);
        services.TryAddKeyedScoped<IWorkflowExecutableStore, GroundworkWorkflowExecutableStore>(
            WorkflowExecutableProviderKey);
        if (cacheOptions.Enabled)
        {
            services.RemoveAll<WorkflowExecutableCache>();
            services.RemoveAll<GroundworkWorkflowExecutableCacheLoader>();
            services.AddSingleton<WorkflowExecutableCache>();
            services.AddSingleton<GroundworkWorkflowExecutableCacheLoader>();
            services.AddScoped<CachingWorkflowExecutableStore>(serviceProvider =>
            {
                var provider = serviceProvider.GetRequiredKeyedService<IWorkflowExecutableStore>(
                    WorkflowExecutableProviderKey);
                var context = serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
                if (context.AccessPolicy != PersistenceAccessPolicy.Ordinary || context.Scope is null)
                    throw new InvalidOperationException(
                        "The workflow executable cache adapter requires an ordinary persistence scope.");

                var persistenceScope = context.Scope;
                var loader = serviceProvider.GetRequiredService<GroundworkWorkflowExecutableCacheLoader>();
                return new CachingWorkflowExecutableStore(
                    provider,
                    serviceProvider.GetRequiredService<WorkflowExecutableCache>(),
                    persistenceScope.Value,
                    (artifactId, cancellationToken) =>
                        loader.LoadAsync(persistenceScope, artifactId, cancellationToken));
            });
            services.AddScoped<InvalidatingWorkflowExecutableStore>(serviceProvider =>
            {
                var provider = serviceProvider.GetRequiredKeyedService<IWorkflowExecutableStore>(
                    WorkflowExecutableProviderKey);
                var context = serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
                return new InvalidatingWorkflowExecutableStore(
                    provider,
                    serviceProvider.GetRequiredService<WorkflowExecutableCache>(),
                    context.Scope?.Value);
            });
            services.AddScoped<IWorkflowExecutableStore>(serviceProvider =>
            {
                var context = serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
                return context.AccessPolicy == PersistenceAccessPolicy.Ordinary && context.Scope is not null
                    ? serviceProvider.GetRequiredService<CachingWorkflowExecutableStore>()
                    : serviceProvider.GetRequiredService<InvalidatingWorkflowExecutableStore>();
            });
        }
        else
        {
            services.AddScoped<IWorkflowExecutableStore>(serviceProvider =>
                serviceProvider.GetRequiredKeyedService<IWorkflowExecutableStore>(WorkflowExecutableProviderKey));
        }
        lane.Replace<IExecutableActivityTemplateStore, GroundworkExecutableActivityTemplateStore>();
        lane.Replace<IWorkflowExecutableSourceReferenceStore, GroundworkWorkflowExecutableSourceReferenceStore>();
        lane.Replace<IActivityExecutionStateStore, GroundworkActivityExecutionStateStore>();
        services.RemoveAll<IActivityExecutionInspectionStore>();
        services.RemoveAll<IActivityExecutionInspectionWriter>();
        services.RemoveAll<GroundworkActivityExecutionInspectionStore>();
        lane.AddSelf<GroundworkActivityExecutionInspectionStore>();
        lane.Alias<IActivityExecutionInspectionStore, GroundworkActivityExecutionInspectionStore>();
        lane.Alias<IActivityExecutionInspectionWriter, GroundworkActivityExecutionInspectionStore>();
        services.RemoveAll<IActivityExecutionHierarchyStore>();
        services.RemoveAll<IActivityExecutionHierarchyReader>();
        services.RemoveAll<IActivityExecutionHierarchyWriter>();
        services.AddScoped<IActivityExecutionHierarchyStore, GroundworkActivityExecutionHierarchyStore>();
        lane.Alias<IActivityExecutionHierarchyReader, IActivityExecutionHierarchyStore>();
        lane.Alias<IActivityExecutionHierarchyWriter, IActivityExecutionHierarchyStore>();
        services.RemoveAll<IWorkflowExecutionStateStore>();
        services.RemoveAll<GroundworkWorkflowExecutionStateStore>();
        services.AddScoped<GroundworkWorkflowExecutionStateStore>(serviceProvider => new GroundworkWorkflowExecutionStateStore(
            lane.Store(serviceProvider),
            serviceProvider.GetRequiredService<IGroundworkRuntimeDocumentSerializer>(),
            serviceProvider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            serviceProvider.GetService<IBoundedDocumentStore>()
            ?? lane.Store(serviceProvider) as IBoundedDocumentStore
            ?? throw new InvalidOperationException("Workflow-execution queries require an admitted bounded document-store runtime.")));
        lane.Alias<IWorkflowExecutionStateStore, GroundworkWorkflowExecutionStateStore>();
        services.RemoveAll<IWorkflowAlterationStore>();
        services.RemoveAll<GroundworkWorkflowAlterationStore>();
        lane.AddSelf<GroundworkWorkflowAlterationStore>();
        lane.Alias<IWorkflowAlterationStore, GroundworkWorkflowAlterationStore>();
        services.RemoveAll<IWorkflowTestScopeStore>();
        services.RemoveAll<IWorkflowTestScopeAdmissionStore>();
        services.RemoveAll<IWorkflowTestScopeCleanupStore>();
        services.RemoveAll<GroundworkWorkflowTestScopeStore>();
        lane.AddSelf<GroundworkWorkflowTestScopeStore>();
        lane.Alias<IWorkflowTestScopeStore, GroundworkWorkflowTestScopeStore>();
        lane.Alias<IWorkflowTestScopeAdmissionStore, GroundworkWorkflowTestScopeStore>();
        lane.Replace<IDurableValueStateStore, GroundworkDurableValueStateStore>();
        lane.Replace<ISchedulerStateStore, GroundworkSchedulerStateStore>();
        lane.Replace<IExecutionLivenessStateStore, GroundworkExecutionLivenessStateStore>();
        lane.Replace<IRuntimeRecoveryScanner, GroundworkRuntimeRecoveryScanner>();
        lane.Replace<IWorkflowHoldStateStore, GroundworkWorkflowHoldStateStore>();
        services.RemoveAll<IIncidentStateStore>();
        services.RemoveAll<GroundworkIncidentStateStore>();
        lane.AddSelf<GroundworkIncidentStateStore>();
        lane.Alias<IIncidentStateStore, GroundworkIncidentStateStore>();
        lane.Replace<IWorkflowRuntimeAttentionQuery, GroundworkWorkflowRuntimeAttentionQuery>();
        services.RemoveAll<IWorkflowDispatchStore>();
        services.RemoveAll<IWorkflowDispatchQueryStore>();
        services.RemoveAll<IWorkflowDispatchDeleteStore>();
        services.RemoveAll<IWorkflowDispatchRetentionRootStore>();
        services.RemoveAll<IWorkflowDispatchAdmissionStore>();
        services.RemoveAll<IWorkflowDispatchCancellationStore>();
        services.RemoveAll<GroundworkWorkflowDispatchStore>();
        lane.AddSelf<GroundworkWorkflowDispatchStore>();
        lane.Alias<IWorkflowDispatchStore, GroundworkWorkflowDispatchStore>();
        lane.Alias<IWorkflowDispatchQueryStore, GroundworkWorkflowDispatchStore>();
        lane.Alias<IWorkflowDispatchDeleteStore, GroundworkWorkflowDispatchStore>();
        lane.Alias<IWorkflowDispatchRetentionRootStore, GroundworkWorkflowDispatchStore>();
        lane.Alias<IWorkflowDispatchAdmissionStore, GroundworkWorkflowDispatchStore>();
        lane.Alias<IWorkflowDispatchCancellationStore, GroundworkWorkflowDispatchStore>();

        // Durable checkpoint writer. It orchestrates the Groundwork-backed seam stores above and records a
        // restart-safe per-CommitId marker, replacing the in-memory writer registered by the runtime feature.
        lane.Replace<IRuntimeCheckpointCommitStore, GroundworkRuntimeCheckpointWriter>();

        services.RemoveAll<IRuntimePostCommitOutboxStore>();
        services.RemoveAll<IPostCommitOutboxLookupStore>();
        services.RemoveAll<IRuntimePostCommitOutboxClaimStore>();
        services.RemoveAll<IRuntimePostCommitOutboxClaimCompletionStore>();
        services.RemoveAll<IWorkflowDispatchRedriveStore>();
        services.RemoveAll<GroundworkRuntimePostCommitOutboxStore>();
        lane.AddSelf<GroundworkRuntimePostCommitOutboxStore>();
        lane.Alias<IRuntimePostCommitOutboxStore, GroundworkRuntimePostCommitOutboxStore>();
        lane.Alias<IPostCommitOutboxLookupStore, GroundworkRuntimePostCommitOutboxStore>();
        lane.Alias<IRuntimePostCommitOutboxClaimStore, GroundworkRuntimePostCommitOutboxStore>();
        lane.Alias<IRuntimePostCommitOutboxClaimCompletionStore, GroundworkRuntimePostCommitOutboxStore>();
        lane.Alias<IWorkflowDispatchRedriveStore, GroundworkRuntimePostCommitOutboxStore>();
        services.AddScoped<IWorkflowTestScopeCleanupStore, GroundworkTestScopeCleanupStore>();

        // Versioned document serialization: every bridge store routes its content JSON through the
        // serializer, which stamps per-kind schema versions on write and enforces each declared readable
        // boundary. TryAdd keeps host-supplied serializer replacements intact.
        services.TryAddSingleton<IGroundworkRuntimeDocumentSerializer, GroundworkRuntimeDocumentSerializer>();
        // Durable scheduler work queue. Without this swap the post-commit outbox delivers into the
        // process-local in-memory queue, and a crash after checkpoint commit loses the continuation
        // even though state and outbox items were stored durably.
        lane.Replace<IWorkflowSchedulerWorkQueue, GroundworkWorkflowSchedulerWorkQueue>();

        // Durable scheduler poison store. Without this swap handler crashes recorded by the drainer live only
        // in process memory and disappear on restart.
        lane.Replace<IWorkflowSchedulerPoisonStore, GroundworkWorkflowSchedulerPoisonStore>();

        // Durable timer store. Without this swap timers live only in the process-local in-memory store and
        // are lost on restart, so a Delay would never resume after a crash.
        lane.Replace<IDurableTimerStore, GroundworkDurableTimerStore>();

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
        lane.Replace<IWorkflowTriggerBindingStore, GroundworkWorkflowTriggerBindingStore>();

        // Durable recurring-trigger schedule store (W16). Without this swap the Timer/Cron schedules written at
        // publish time live only in the process-local in-memory store, so a restart forgets every recurring
        // start trigger until the workflow is republished.
        lane.Replace<IRecurringTriggerScheduleStore, GroundworkRecurringTriggerScheduleStore>();

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
