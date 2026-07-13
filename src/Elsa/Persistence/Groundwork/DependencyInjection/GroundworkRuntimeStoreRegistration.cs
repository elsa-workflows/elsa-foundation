using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
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

    /// <summary>Registers Groundwork runtime stores with direct executable-provider reads for compatibility.</summary>
    public static IServiceCollection AddGroundworkRuntimeStores(this IServiceCollection services) =>
        services.AddGroundworkRuntimeStores(new WorkflowExecutableCacheOptions { Enabled = false });

    /// <summary>Registers Groundwork runtime stores with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkRuntimeStores(
        this IServiceCollection services,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);

        var cacheOptions = new WorkflowExecutableCacheOptions
        {
            Enabled = workflowExecutableCacheOptions.Enabled,
            Capacity = workflowExecutableCacheOptions.Capacity
        };
        cacheOptions.Validate();

        // Replace the in-memory defaults registered by the runtime API feature. RemoveAll guarantees
        // the bridge wins regardless of feature composition order.
        services.RemoveAll<IBookmarkStateStore>();
        services.AddSingleton<IBookmarkStateStore, GroundworkBookmarkStateStore>();
        services.RemoveAll<IWorkflowExecutableStore>();
        services.RemoveAll<CachingWorkflowExecutableStore>();
        services.RemoveAll<WorkflowExecutableCacheOptions>();
        services.AddSingleton(cacheOptions);
        services.TryAddKeyedSingleton<IWorkflowExecutableStore, GroundworkWorkflowExecutableStore>(WorkflowExecutableProviderKey);
        if (cacheOptions.Enabled)
        {
            services.AddSingleton(serviceProvider => new CachingWorkflowExecutableStore(
                serviceProvider.GetRequiredKeyedService<IWorkflowExecutableStore>(WorkflowExecutableProviderKey),
                serviceProvider.GetRequiredService<WorkflowExecutableCacheOptions>()));
            services.AddSingleton<IWorkflowExecutableStore>(serviceProvider =>
                serviceProvider.GetRequiredService<CachingWorkflowExecutableStore>());
        }
        else
        {
            services.AddSingleton<IWorkflowExecutableStore>(serviceProvider =>
                serviceProvider.GetRequiredKeyedService<IWorkflowExecutableStore>(WorkflowExecutableProviderKey));
        }
        services.RemoveAll<IWorkflowExecutableSourceReferenceStore>();
        services.AddSingleton<IWorkflowExecutableSourceReferenceStore, GroundworkWorkflowExecutableSourceReferenceStore>();
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
        services.RemoveAll<IExecutionLivenessStateStore>();
        services.AddSingleton<IExecutionLivenessStateStore, GroundworkExecutionLivenessStateStore>();
        services.RemoveAll<IWorkflowHoldStateStore>();
        services.AddSingleton<IWorkflowHoldStateStore, GroundworkWorkflowHoldStateStore>();
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGroundworkRuntimeDocumentUpcaster, WorkflowExecutableDocumentV1ToV2Upcaster>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGroundworkRuntimeDocumentUpcaster, WorkflowExecutionStateDocumentV1ToV2Upcaster>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGroundworkRuntimeDocumentUpcaster, WorkflowExecutableSourceReferenceDocumentV1ToV2Upcaster>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGroundworkRuntimeDocumentUpcaster, WorkflowTriggerBindingDocumentV1ToV2Upcaster>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGroundworkRuntimeDocumentUpcaster, RecurringTriggerScheduleDocumentV1ToV2Upcaster>());
        services.TryAddSingleton<IGroundworkRuntimeDocumentUpcasterRegistry, GroundworkRuntimeDocumentUpcasterRegistry>();

        // Durable scheduler work queue. Without this swap the post-commit outbox delivers into the
        // process-local in-memory queue, and a crash after checkpoint commit loses the continuation
        // even though state and outbox items were stored durably.
        services.RemoveAll<IWorkflowSchedulerWorkQueue>();
        services.AddSingleton<IWorkflowSchedulerWorkQueue, GroundworkWorkflowSchedulerWorkQueue>();

        // Durable timer store. Without this swap timers live only in the process-local in-memory store and
        // are lost on restart, so a Delay would never resume after a crash.
        services.RemoveAll<IDurableTimerStore>();
        services.AddSingleton<IDurableTimerStore, GroundworkDurableTimerStore>();

        // Durable trigger index (W7, E3-1). Without this swap the trigger bindings written at publish time
        // live only in the process-local in-memory store, so a restart loses the ability to start workflows
        // from a stimulus even though the published executable is durable.
        services.RemoveAll<IWorkflowTriggerBindingStore>();
        services.AddSingleton<IWorkflowTriggerBindingStore, GroundworkWorkflowTriggerBindingStore>();

        // Durable recurring-trigger schedule store (W16). Without this swap the Timer/Cron schedules written at
        // publish time live only in the process-local in-memory store, so a restart forgets every recurring
        // start trigger until the workflow is republished.
        services.RemoveAll<IRecurringTriggerScheduleStore>();
        services.AddSingleton<IRecurringTriggerScheduleStore, GroundworkRecurringTriggerScheduleStore>();

        return services;
    }
}
