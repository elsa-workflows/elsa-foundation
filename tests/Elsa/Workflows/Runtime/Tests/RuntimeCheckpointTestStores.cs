using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Builds a complete in-memory checkpoint composition for tests that commit real runtime state.</summary>
internal static class RuntimeCheckpointTestStores
{
    public static InMemoryRuntimeCheckpointCommitStore Create(
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
        IActivityExecutionStateStore? activityExecutionStateStore = null,
        IBookmarkStateStore? bookmarkStateStore = null,
        IDurableValueStateStore? durableValueStateStore = null,
        IIncidentStateStore? incidentStateStore = null,
        IExecutionLivenessStateStore? operationalStateStore = null,
        ISchedulerStateStore? schedulerStateStore = null,
        IActivityExecutionInspectionWriter? activityExecutionInspectionWriter = null,
        IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager = null,
        InMemoryRuntimeCheckpointStoreState? state = null,
        TimeProvider? timeProvider = null,
        IActivityScopeCleanupStore? activityScopeCleanupStore = null,
        IActivityExecutionHierarchyWriter? activityExecutionHierarchyWriter = null,
        IWorkflowDispatchStore? workflowDispatchStore = null,
        IWorkflowSchedulerWorkQueue? schedulerWorkQueue = null,
        IWorkflowAlterationStore? alterationStore = null,
        IDurableTimerStore? durableTimerStore = null)
    {
        state ??= new InMemoryRuntimeCheckpointStoreState();
        bookmarkStateStore ??= new InMemoryBookmarkStateStore();
        durableTimerStore ??= new InMemoryDurableTimerStore();
        schedulerWorkQueue ??= new InMemoryWorkflowSchedulerWorkQueue();

        return new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore ?? new InMemoryWorkflowExecutionStateStore(),
            activityExecutionStateStore ?? new InMemoryActivityExecutionStateStore(),
            bookmarkStateStore,
            durableValueStateStore ?? new InMemoryDurableValueStateStore(),
            incidentStateStore ?? new InMemoryIncidentStateStore(),
            operationalStateStore ?? new InMemoryExecutionLivenessStateStore(),
            schedulerStateStore ?? new InMemorySchedulerStateStore(),
            activityExecutionInspectionWriter ?? new InMemoryActivityExecutionInspectionStore(),
            rootWriteLeaseManager ?? PassThroughWorkflowExecutableRootWriteLeaseManager.Instance,
            state,
            timeProvider,
            activityScopeCleanupStore ?? new ActivityScopeCleanupStore(bookmarkStateStore, durableTimerStore, schedulerWorkQueue),
            activityExecutionHierarchyWriter ?? new RuntimeInMemoryActivityExecutionHierarchyStore(
                new HmacActivityExecutionHierarchyCursorCodec(Options.Create(new ActivityExecutionHierarchyCursorOptions
                {
                    SigningKey = "runtime-checkpoint-test-store-composition-key"
                }))),
            workflowDispatchStore ?? new InMemoryWorkflowDispatchStore(state),
            schedulerWorkQueue,
            alterationStore ?? new InMemoryWorkflowAlterationStore());
    }
}
