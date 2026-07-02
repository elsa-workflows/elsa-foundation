using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// ADR 0029 Move 2 (first slice): the workflow <c>Checkpoint</c> slot persists the commit a terminal handler stages on
/// the dispatch workspace. Covers the middleware, the Cancel handler's staging, and the end-to-end path through the
/// feature-composed pipeline. Cancel is the only handler migrated in this slice; behaviour is preserved.
/// </summary>
public sealed class RuntimeCheckpointSlotDecompositionTests
{
    [Fact]
    public async Task CheckpointMiddleware_CommitsStagedCommit()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var middleware = new RuntimeWorkflowCheckpointMiddleware(NewCommitter(store));
        var context = new WorkflowRuntimePipelineContext(NewCancelWorkItem())
        {
            Workspace = { PendingCheckpointCommit = NewCommit() }
        };

        await middleware.InvokeAsync(context, _ => ValueTask.CompletedTask);

        Assert.Single(store.ListCommits());
    }

    [Fact]
    public async Task CheckpointMiddleware_DoesNothing_WhenNoCommitStaged()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var middleware = new RuntimeWorkflowCheckpointMiddleware(NewCommitter(store));
        var context = new WorkflowRuntimePipelineContext(NewCancelWorkItem());

        await middleware.InvokeAsync(context, _ => ValueTask.CompletedTask);

        Assert.Empty(store.ListCommits());
    }

    [Fact]
    public async Task CancelHandler_StagesCommitForTheCheckpointSlot_WhenAPipelineContextIsAmbient()
    {
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var activityStore = new InMemoryActivityExecutionStateStore();
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore();
        await workflowStore.SaveAsync(NewWorkflowState(WorkflowExecutionStatus.Running));
        await activityStore.SaveAsync(NewRunningActivityState());
        var accessor = new AsyncLocalRuntimePipelineContextAccessor();
        var handler = NewCancelHandler(workflowStore, activityStore, checkpointStore, accessor);
        var context = new WorkflowRuntimePipelineContext(NewCancelWorkItem());

        using (accessor.Push(context))
            await handler.HandleAsync(NewCancelWorkItem());

        // The handler staged the commit for the Checkpoint slot rather than committing inline.
        Assert.Empty(checkpointStore.ListCommits());
        var staged = context.Workspace.PendingCheckpointCommit;
        Assert.NotNull(staged);
        Assert.Equal(RuntimeCheckpointNames.ActivityCancelled, staged.Checkpoint.Name);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, staged.StateChanges.WorkflowExecution!.State.Status);
    }

    [Fact]
    public async Task CancelHandler_CommitsInline_WhenNoPipelineContextIsAmbient()
    {
        // Behaviour-preserving fallback: constructed/dispatched without a pipeline, the handler commits as before.
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var activityStore = new InMemoryActivityExecutionStateStore();
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore();
        await workflowStore.SaveAsync(NewWorkflowState(WorkflowExecutionStatus.Running));
        await activityStore.SaveAsync(NewRunningActivityState());
        var handler = NewCancelHandler(workflowStore, activityStore, checkpointStore, new AsyncLocalRuntimePipelineContextAccessor());

        await handler.HandleAsync(NewCancelWorkItem());

        Assert.Single(checkpointStore.ListCommits());
    }

    [Fact]
    public async Task Cancel_ThroughFeaturePipeline_CommitsViaCheckpointSlot()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(NewWorkflowState(WorkflowExecutionStatus.Running));
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewRunningActivityState());
        var dispatcher = provider.GetRequiredService<IRuntimeExecutionPipelineDispatcher>();
        var cancelHandler = provider.GetServices<IWorkflowSchedulerWorkHandler>().OfType<WorkflowCancelSchedulerWorkHandler>().Single();

        await dispatcher.DispatchAsync(NewCancelWorkItem(), cancelHandler);

        var committed = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityCancelled, committed.Commit.Checkpoint.Name);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, committed.Commit.StateChanges.WorkflowExecution!.State.Status);
    }

    private static WorkflowCancelSchedulerWorkHandler NewCancelHandler(
        IWorkflowExecutionStateStore workflowStore,
        IActivityExecutionStateStore activityStore,
        IRuntimeCheckpointCommitStore checkpointStore,
        IRuntimePipelineContextAccessor accessor) =>
        new(
            workflowStore,
            activityStore,
            NewCommitter(checkpointStore),
            new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore()),
            TimeProvider.System,
            accessor);

    private static RuntimeCheckpointCommitter NewCommitter(IRuntimeCheckpointCommitStore store) =>
        new(new ImmediateRuntimeCheckpointPersistencePolicy(), store);

    private static RuntimeCheckpointCommit NewCommit() =>
        new(
            CommitId: "commit:test",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint:test",
                Name: RuntimeCheckpointNames.ActivityCancelled,
                WorkflowExecutionId: "wf-1",
                OccurredAt: DateTimeOffset.UnixEpoch,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private static RuntimeSchedulerWorkItem NewCancelWorkItem() =>
        new(
            workItemId: "cancel-work",
            workflowExecutionId: "wf-1",
            commandId: "command-cancel",
            commandKind: WorkflowExecutionCommandKind.Cancel,
            envelopeId: "envelope-cancel",
            idempotencyKey: "wf-1:cancel",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 10);

    private static WorkflowExecutionState NewWorkflowState(WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: "wf-1",
            PinnedExecutable: NewIdentity(),
            Status: status,
            SubStatus: null,
            CreatedAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    private static ActivityExecutionState NewRunningActivityState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-running",
                WorkflowExecutionId: "wf-1",
                ExecutableNodeId: "node-a",
                AuthoredActivityId: "authored-a",
                ActivityType: "Elsa.Test",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.From(
                "wf-1",
                parentActivityExecutionId: null,
                schedulingActivityExecutionId: null,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
}
