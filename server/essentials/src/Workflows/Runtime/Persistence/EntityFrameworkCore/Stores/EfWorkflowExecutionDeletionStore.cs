using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Exposes run-history deletion only when the complete selected Runtime persistence family is EF-owned.</summary>
public sealed class EfWorkflowExecutionDeletionStore(
    IServiceProvider serviceProvider,
    EfWorkflowExecutionStateStore executionStateStore) : IWorkflowExecutionDeletionStore
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly EfWorkflowExecutionStateStore _executionStateStore = executionStateStore ?? throw new ArgumentNullException(nameof(executionStateStore));

    public bool IsSupported => HasCompleteEfOwnership() && HasSelectedEfStores();

    public ValueTask<WorkflowExecutionDeletionCandidate?> ReadAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        return _executionStateStore.ReadDeletionCandidateAsync(workflowExecutionId, cancellationToken);
    }

    public ValueTask<WorkflowExecutionDeletionResult> TryDeleteTerminalAsync(
        WorkflowExecutionDeletionCandidate candidate,
        CancellationToken cancellationToken = default) =>
        IsSupported
            ? _executionStateStore.TryDeleteTerminalAsync(candidate, cancellationToken)
            : ValueTask.FromResult(WorkflowExecutionDeletionResult.Unsupported);

    private void EnsureSupported()
    {
        if (!IsSupported)
            throw new NotSupportedException("Workflow execution deletion requires the complete EF Runtime persistence composition.");
    }

    private bool HasCompleteEfOwnership() =>
        IsEf<WorkflowExecutionStateStoreBackend>(backend => backend.Name == WorkflowExecutionStateStoreBackend.EntityFramework) &&
        IsEf<RuntimeActivityExecutionStoreBackend>(backend => backend.Name == RuntimeActivityExecutionStoreBackend.EntityFramework) &&
        IsEf<BookmarkStateStoreBackend>(backend => backend.Name == BookmarkStateStoreBackend.EntityFramework) &&
        IsEf<RuntimeOperationalStateStoreBackend>(backend => backend.Name == RuntimeOperationalStateStoreBackend.EntityFramework) &&
        IsEf<DurableTimerStoreBackend>(backend => backend.Name == DurableTimerStoreBackend.EntityFramework) &&
        IsEf<SchedulerWorkQueueStoreBackend>(backend => backend.Name == SchedulerWorkQueueStoreBackend.EntityFramework) &&
        IsEf<WorkflowSchedulerPoisonStoreBackend>(backend => backend.Name == WorkflowSchedulerPoisonStoreBackend.EntityFramework) &&
        IsEf<RuntimeCheckpointCommitStoreBackend>(backend => backend.Name == RuntimeCheckpointCommitStoreBackend.EntityFramework) &&
        IsEf<RuntimePostCommitOutboxStoreBackend>(backend => backend.Name == RuntimePostCommitOutboxStoreBackend.EntityFramework) &&
        IsEf<RuntimeWorkflowDispatchStoreBackend>(backend => backend.Name == RuntimeWorkflowDispatchStoreBackend.EntityFramework) &&
        IsEf<RuntimeArtifactStoreBackend>(backend => backend.Name == RuntimeArtifactStoreBackend.EntityFramework) &&
        IsEf<RuntimeWorkflowAlterationStoreBackend>(backend => backend.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework) &&
        IsEf<WorkflowTestScopeStoreBackend>(backend => backend.Name == WorkflowTestScopeStoreBackend.EntityFramework) &&
        IsEf<WorkflowTriggerBindingStoreBackend>(backend => backend.Name == WorkflowTriggerBindingStoreBackend.EntityFramework) &&
        IsEf<RecurringTriggerScheduleStoreBackend>(backend => backend.Name == RecurringTriggerScheduleStoreBackend.EntityFramework) &&
        IsEf<WorkflowActivationAuthorityBackend>(backend => backend.Name == WorkflowActivationAuthorityBackend.EntityFramework) &&
        _serviceProvider.GetService<RuntimeBackgroundActivityWorkEfRegistration>() is not null;

    private bool HasSelectedEfStores()
    {
        return IsSelected<EfWorkflowExecutionStateStore, IWorkflowExecutionStateStore>(
                   typeof(CoalescingWorkflowExecutionStateStore)) &&
               IsSelected<EfActivityExecutionStateStore, IActivityExecutionStateStore>(
                   typeof(CoalescingActivityExecutionStateStore)) &&
               IsSelected<EfActivityExecutionInspectionStore, IActivityExecutionInspectionStore>(
                   typeof(CoalescingActivityExecutionInspectionStore)) &&
               IsSelected<EfActivityExecutionInspectionStore, IActivityExecutionInspectionWriter>() &&
               IsSelected<EfActivityExecutionHierarchyStore, IActivityExecutionHierarchyStore>() &&
               IsSelected<EfActivityExecutionHierarchyStore, IActivityExecutionHierarchyWriter>() &&
               IsSelected<EfBookmarkStateStore, IBookmarkStateStore>() &&
               IsSelected<EfDurableValueStateStore, IDurableValueStateStore>(
                   typeof(CoalescingDurableValueStateStore)) &&
               IsSelected<EfSchedulerStateStore, ISchedulerStateStore>(
                   typeof(CoalescingSchedulerStateStore)) &&
               IsSelected<EfDurableTimerStore, IDurableTimerStore>() &&
               IsSelected<EfSchedulerWorkQueueStore, IWorkflowSchedulerWorkQueue>(
                   typeof(CoalescingWorkflowSchedulerWorkQueue)) &&
               IsSelected<EfIncidentStateStore, IIncidentStateStore>() &&
               IsSelected<EfWorkflowHoldStateStore, IWorkflowHoldStateStore>() &&
               IsSelected<EfWorkflowSchedulerPoisonStore, IWorkflowSchedulerPoisonStore>() &&
               IsSelected<EfExecutionLivenessStateStore, IExecutionLivenessStateStore>() &&
               IsSelected<EfBackgroundActivityWorkStore, IBackgroundActivityWorkStore>() &&
               IsSelected<EfRuntimeCheckpointCommitStore, IRuntimeCheckpointCommitStore>(
                   typeof(CoalescingRuntimeCheckpointCommitStore)) &&
               IsSelected<EfRuntimePostCommitOutboxStore, IRuntimePostCommitOutboxStore>(
                   typeof(CoalescingRuntimePostCommitOutboxStore)) &&
               IsSelected<EfWorkflowDispatchStore, IWorkflowDispatchStore>();
    }

    private bool IsEf<TBackend>(Func<TBackend, bool> predicate)
        where TBackend : class =>
        _serviceProvider.GetService<TBackend>() is { } backend && predicate(backend);

    private bool IsSelected<TEfStore, TContract>(Type? coalescingDecorator = null)
        where TEfStore : class
        where TContract : class
    {
        var selected = _serviceProvider.GetService<TContract>();
        if (selected is null)
            return false;
        if (selected.GetType() == typeof(TEfStore))
            return true;
        if (coalescingDecorator is null || selected.GetType() != coalescingDecorator)
            return false;

        return _serviceProvider.GetService<CoalescingInner<TContract>>()?.Value.GetType() == typeof(TEfStore);
    }
}
