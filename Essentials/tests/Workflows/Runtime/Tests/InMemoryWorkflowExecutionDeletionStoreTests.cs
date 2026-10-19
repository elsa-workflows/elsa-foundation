using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class InMemoryWorkflowExecutionDeletionStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryDeleteTerminalAsync_PurgesRunOwnedStateAndPreservesOtherRunState()
    {
        using var fixture = new RuntimeFixture();
        Assert.True(fixture.DeletionStore.IsSupported);

        await SeedRunAsync(fixture, "run-delete", WorkflowExecutionStatus.Completed);
        await SeedRunAsync(fixture, "run-keep", WorkflowExecutionStatus.Running);
        var candidate = await fixture.DeletionStore.ReadAsync("run-delete");

        Assert.NotNull(candidate);
        Assert.Equal(
            WorkflowExecutionDeletionResult.Deleted,
            await fixture.DeletionStore.TryDeleteTerminalAsync(candidate));

        Assert.Null(await fixture.Executions.FindAsync("run-delete"));
        Assert.NotNull(await fixture.Executions.FindAsync("run-keep"));
        Assert.Null(await fixture.ActivityExecutions.FindAsync("run-delete", "activity-run-delete"));
        Assert.NotNull(await fixture.ActivityExecutions.FindAsync("run-keep", "activity-run-keep"));
        Assert.Null(await fixture.Inspections.FindAsync("run-delete", "activity-run-delete"));
        Assert.NotNull(await fixture.Inspections.FindAsync("run-keep", "activity-run-keep"));
        Assert.Null(await fixture.Bookmarks.FindAsync("run-delete", "bookmark-run-delete"));
        Assert.NotNull(await fixture.Bookmarks.FindAsync("run-keep", "bookmark-run-keep"));
        Assert.Null(await fixture.DurableValues.FindAsync("run-delete", "value-run-delete"));
        Assert.NotNull(await fixture.DurableValues.FindAsync("run-keep", "value-run-keep"));
        Assert.Null(await fixture.SchedulerStates.FindAsync("run-delete"));
        Assert.NotNull(await fixture.SchedulerStates.FindAsync("run-keep"));
        Assert.Null(await fixture.Timers.FindAsync("run-delete", "timer-run-delete"));
        Assert.NotNull(await fixture.Timers.FindAsync("run-keep", "timer-run-keep"));
        Assert.Empty(await fixture.SchedulerWork.ListAllAsync(new RuntimeSchedulerWorkQuery("run-delete")));
        Assert.Single(await fixture.SchedulerWork.ListAllAsync(new RuntimeSchedulerWorkQuery("run-keep")));
        Assert.Null(await fixture.IncidentStates.FindAsync("run-delete", "incident-run-delete"));
        Assert.NotNull(await fixture.IncidentStates.FindAsync("run-keep", "incident-run-keep"));
        Assert.Null(await fixture.Poison.FindAsync("run-delete", "poison-run-delete"));
        Assert.NotNull(await fixture.Poison.FindAsync("run-keep", "poison-run-keep"));
        Assert.Empty(await fixture.Holds.ListForWorkflowExecutionAsync("run-delete"));
        Assert.Single(await fixture.Holds.ListForWorkflowExecutionAsync("run-keep"));

        Assert.Null(await fixture.Hierarchy.FindLayoutAsync("run-delete", "activity-run-delete"));
        Assert.NotNull(await fixture.Hierarchy.FindLayoutAsync("run-keep", "activity-run-keep"));

        var tombstone = await fixture.Liveness.FindAsync(
            "run-delete",
            RuntimeExecutionOwnershipStateId.For("run-delete"));
        Assert.Equal("true", tombstone!.Metadata[RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone]);
    }

    [Fact]
    public async Task TryDeleteTerminalAsync_RejectsNonterminalAndChangedSnapshotsWithoutMutation()
    {
        using var fixture = new RuntimeFixture();
        await fixture.Executions.SaveAsync(Workflow("run-active", WorkflowExecutionStatus.Running));
        await fixture.Executions.SaveAsync(Workflow("run-changed", WorkflowExecutionStatus.Completed));

        var activeCandidate = await fixture.DeletionStore.ReadAsync("run-active");
        Assert.NotNull(activeCandidate);
        Assert.Equal(
            WorkflowExecutionDeletionResult.NotTerminal,
            await fixture.DeletionStore.TryDeleteTerminalAsync(activeCandidate));
        Assert.NotNull(await fixture.Executions.FindAsync("run-active"));
        Assert.Null(await fixture.Liveness.FindAsync("run-active", RuntimeExecutionOwnershipStateId.For("run-active")));

        var staleCandidate = await fixture.DeletionStore.ReadAsync("run-changed");
        Assert.NotNull(staleCandidate);
        var changedState = staleCandidate.State with
        {
            SystemMetadata = new Dictionary<string, string> { ["changed"] = "true" }
        };
        await fixture.Executions.SaveAsync(changedState);

        Assert.Equal(
            WorkflowExecutionDeletionResult.Changed,
            await fixture.DeletionStore.TryDeleteTerminalAsync(staleCandidate));
        Assert.Same(changedState, await fixture.Executions.FindAsync("run-changed"));
        Assert.Null(await fixture.Liveness.FindAsync("run-changed", RuntimeExecutionOwnershipStateId.For("run-changed")));
    }

    [Fact]
    public async Task TryDeleteTerminalAsync_CancelsPendingBackgroundWorkAndRetainsItsReceiptRow()
    {
        using var fixture = new RuntimeFixture();
        await fixture.Executions.SaveAsync(Workflow("run-background", WorkflowExecutionStatus.Cancelled));
        var request = BackgroundWorkRequest("run-background");
        var key = ("scope-a", request.WorkflowExecutionId, request.WorkId);
        fixture.CheckpointState.BackgroundActivityWorkItems[key] = BackgroundActivityWorkLifecycle.CreatePending(request, Now);
        var candidate = await fixture.DeletionStore.ReadAsync("run-background");

        Assert.Equal(
            WorkflowExecutionDeletionResult.Deleted,
            await fixture.DeletionStore.TryDeleteTerminalAsync(candidate!));

        Assert.True(fixture.CheckpointState.BackgroundActivityWorkItems.TryGetValue(key, out var cancelled));
        Assert.Equal(BackgroundActivityWorkStatus.Cancelled, cancelled!.Status);
    }

    [Fact]
    public async Task TryDeleteTerminalAsync_SerializesDirectSaveAgainstSnapshotCleanup()
    {
        using var fixture = new RuntimeFixture();
        await SeedRunAsync(fixture, "run-race", WorkflowExecutionStatus.Completed);
        var candidate = await fixture.DeletionStore.ReadAsync("run-race");
        Assert.NotNull(candidate);
        var replacement = candidate.State with
        {
            SystemMetadata = new Dictionary<string, string> { ["revision"] = "replacement" }
        };
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var saveTask = Task.Run(async () =>
        {
            await start.Task;
            await fixture.Executions.SaveAsync(replacement);
        });
        var deleteTask = Task.Run(async () =>
        {
            await start.Task;
            return await fixture.DeletionStore.TryDeleteTerminalAsync(candidate);
        });
        start.SetResult();

        var deleteResult = await deleteTask;
        if (deleteResult == WorkflowExecutionDeletionResult.Deleted)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await saveTask);
            Assert.Null(await fixture.Executions.FindAsync("run-race"));
            Assert.Null(await fixture.Bookmarks.FindAsync("run-race", "bookmark-run-race"));
        }
        else
        {
            Assert.Equal(WorkflowExecutionDeletionResult.Changed, deleteResult);
            await saveTask;
            Assert.Same(replacement, await fixture.Executions.FindAsync("run-race"));
            Assert.NotNull(await fixture.Bookmarks.FindAsync("run-race", "bookmark-run-race"));
            Assert.Null(await fixture.Liveness.FindAsync("run-race", RuntimeExecutionOwnershipStateId.For("run-race")));
        }
    }

    [Fact]
    public async Task TryDeleteTerminalAsync_CancellationWhileWaitingLeavesAllStateUntouched()
    {
        using var fixture = new RuntimeFixture();
        await SeedRunAsync(fixture, "run-cancel", WorkflowExecutionStatus.Completed);
        var candidate = await fixture.DeletionStore.ReadAsync("run-cancel");
        Assert.NotNull(candidate);

        await fixture.CheckpointState.WriteGate.WaitAsync();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var deleteTask = fixture.DeletionStore.TryDeleteTerminalAsync(candidate, cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await deleteTask);
        }
        finally
        {
            fixture.CheckpointState.WriteGate.Release();
        }

        Assert.NotNull(await fixture.Executions.FindAsync("run-cancel"));
        Assert.NotNull(await fixture.Bookmarks.FindAsync("run-cancel", "bookmark-run-cancel"));
        Assert.Null(await fixture.Liveness.FindAsync("run-cancel", RuntimeExecutionOwnershipStateId.For("run-cancel")));
    }

    [Fact]
    public async Task TryDeleteTerminalAsync_LeavesOwnershipTombstoneSticky()
    {
        using var fixture = new RuntimeFixture();
        await fixture.Executions.SaveAsync(Workflow("run-tombstone", WorkflowExecutionStatus.Completed));
        var candidate = await fixture.DeletionStore.ReadAsync("run-tombstone");
        Assert.NotNull(candidate);

        Assert.Equal(
            WorkflowExecutionDeletionResult.Deleted,
            await fixture.DeletionStore.TryDeleteTerminalAsync(candidate));

        var ownershipState = await fixture.Liveness.FindVersionedAsync(
            "run-tombstone",
            RuntimeExecutionOwnershipStateId.For("run-tombstone"));
        Assert.NotNull(ownershipState);
        var staleOwnership = new ExecutionLivenessState(
            ownershipState.State.ExecutionLivenessStateId,
            ownershipState.State.WorkflowExecutionId,
            ownershipState.State.ExecutionLease,
            ownershipState.State.Heartbeat,
            ownershipState.State.Drain,
            ownershipState.State.InterruptedExecution,
            ownershipState.State.PendingPostCommitIntentIds,
            new Dictionary<string, string>());

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Liveness.SaveAsync(staleOwnership));
        Assert.Equal(
            ExecutionLivenessStateWriteStatus.RevisionConflict,
            (await fixture.Liveness.TrySaveAsync(staleOwnership, ownershipState.Revision)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new RuntimeExecutionOwnershipService(fixture.Liveness).AcquireAsync("run-tombstone"));

        var retained = await fixture.Liveness.FindAsync(
            "run-tombstone",
            RuntimeExecutionOwnershipStateId.For("run-tombstone"));
        Assert.Equal("true", retained!.Metadata[RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone]);
    }

    [Fact]
    public async Task Retained_scheduler_outbox_delivery_settles_without_recreating_deleted_run_work()
    {
        using var fixture = new RuntimeFixture();
        await SeedRunAsync(fixture, "run-outbox", WorkflowExecutionStatus.Completed);
        var workItem = new RuntimeSchedulerWorkItem("late-work", "run-outbox", "command", WorkflowExecutionCommandKind.StartActivity,
            "envelope", "late-key", Now, Now, 1);
        var store = new InMemoryRuntimeCheckpointCommitStore(state: fixture.CheckpointState);
        await store.AddPendingForTestingAsync(new RuntimePostCommitOutboxItem("late-outbox",
            new RuntimePostCommitIntent("late-intent", "run-outbox", RuntimePostCommitIntentKinds.EnqueueSchedulerWork, Now,
                activityExecutionId: null, idempotencyKey: "late-intent", payload: JsonSerializer.SerializeToElement(workItem)),
            status: RuntimePostCommitOutboxStatus.Pending, recordedAt: Now, availableAt: Now));
        var candidate = await fixture.DeletionStore.ReadAsync("run-outbox");
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await fixture.DeletionStore.TryDeleteTerminalAsync(candidate!));
        var processor = new RuntimePostCommitOutboxProcessor(store, new RuntimeSchedulerPostCommitIntentDispatcher(fixture.SchedulerWork),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));
        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));
        Assert.Equal(1, result.DeliveredCount);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await store.FindAsync("late-outbox"))!.Status);
        Assert.Empty(await fixture.SchedulerWork.ListAllAsync(new RuntimeSchedulerWorkQuery("run-outbox")));
        Assert.Null(await fixture.Executions.FindAsync("run-outbox"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Independent_inspection_or_checkpoint_replacement_disables_deletion(bool replaceCheckpoint)
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddPersistenceModes();
        if (replaceCheckpoint) services.AddSingleton<IRuntimeCheckpointCommitStore>(new InMemoryRuntimeCheckpointCommitStore());
        else services.AddSingleton<IActivityExecutionInspectionStore>(new InMemoryActivityExecutionInspectionStore());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>().IsSupported);
    }

    [Fact]
    public void Later_store_replacement_does_not_reuse_a_stale_coalescing_inner()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowRuntime();
        services.AddPersistenceModes();
        services.AddSingleton<IWorkflowExecutionStateStore>(new InMemoryWorkflowExecutionStateStore());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>().IsSupported);
    }

    private static async Task SeedRunAsync(RuntimeFixture fixture, string workflowExecutionId, WorkflowExecutionStatus status)
    {
        var activity = Activity(workflowExecutionId);
        await fixture.Executions.SaveAsync(Workflow(workflowExecutionId, status));
        await fixture.ActivityExecutions.SaveAsync(activity);
        await fixture.InspectionWriter.SaveAsync(ActivityExecutionInspectionProjection.FromState(activity, "checkpoint", Now));
        await fixture.HierarchyWriter.SaveAsync(HierarchyRecord(workflowExecutionId));
        fixture.Hierarchy.SaveLayout(new ActivityExecutionLayout(
            workflowExecutionId,
            activity.Execution.ActivityExecutionId,
            "artifact",
            "source-reference",
            ActivityExecutionLayoutSelection.ExecutedReference,
            ActivityInvocationOrigin.Empty,
            "template-hash",
            [],
            [],
            []));
        await fixture.Bookmarks.SaveAsync(new BookmarkState(
            $"bookmark-{workflowExecutionId}", workflowExecutionId, activity.Execution.ActivityExecutionId,
            "node", "resume", "signal", "hash", null, new Dictionary<string, string>(), Now, null));
        await fixture.DurableValues.SaveAsync(new DurableValueState(
            $"value-{workflowExecutionId}", workflowExecutionId, "value",
            new RuntimeValueTypeDescriptor("reference", "test", null),
            DurableValueLifecycle.Instance, DurableValueStorage.Inline,
            JsonSerializer.SerializeToElement(new { value = workflowExecutionId }), null,
            activity.Execution.ActivityExecutionId, Now, new Dictionary<string, string>()));
        await fixture.SchedulerStates.SaveAsync(new SchedulerState(workflowExecutionId, 1, [], [], []));
        await fixture.Timers.SaveAsync(new DurableTimer(
            $"timer-{workflowExecutionId}", workflowExecutionId, "DurableTimer", "hash", Now, Now));
        await fixture.SchedulerWork.EnqueueAsync(new RuntimeSchedulerWorkItem(
            $"work-{workflowExecutionId}", workflowExecutionId, "command", WorkflowExecutionCommandKind.StartActivity,
            "envelope", $"key-{workflowExecutionId}", Now, Now, 1));
        await fixture.IncidentStates.SaveAsync(new IncidentState(
            $"incident-{workflowExecutionId}", workflowExecutionId, activity.Execution.ActivityExecutionId, "node",
            IncidentSeverity.Error, IncidentStatus.Blocking, null, "failure", "message", Now, null));
        await fixture.Poison.RecordAsync(new RuntimeSchedulerPoisonRecord(
            workflowExecutionId, $"poison-{workflowExecutionId}", WorkflowExecutionCommandKind.StartActivity,
            "handler", new RuntimeFaultInfo("Exception", "fault"), 1, RuntimeSchedulerPoisonDisposition.Poisoned, Now, Now));
        await fixture.Holds.SaveAsync(new WorkflowHoldState(
            $"hold-{workflowExecutionId}", workflowExecutionId,
            activeHolds: [WorkflowHold.ForWorkflowExecution($"pause-{workflowExecutionId}", workflowExecutionId, Now, "operator", "test hold")]));
    }

    private static WorkflowExecutionState Workflow(string workflowExecutionId, WorkflowExecutionStatus status) => new(
        workflowExecutionId,
        new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "hash"),
        status,
        null,
        Now,
        Now,
        Now,
        status.IsTerminal() ? Now : null,
        null,
        null,
        "tenant-a",
        new Dictionary<string, string>());

    private static ActivityExecutionState Activity(string workflowExecutionId) => new(
        new ActivityExecution($"activity-{workflowExecutionId}", workflowExecutionId, "node", "authored", "test.activity", "1"),
        ActivityExecutionStatus.Completed,
        null,
        1,
        Now,
        Now,
        Now,
        null,
        null,
        "branch",
        null,
        ActivitySchedulingProvenance.From(workflowExecutionId, null, null, "branch", null, null, null, "test"),
        0,
        [],
        [],
        0,
        0,
        new Dictionary<string, string>());

    private static ActivityExecutionHierarchyRecord HierarchyRecord(string workflowExecutionId)
    {
        var activityExecutionId = $"activity-{workflowExecutionId}";
        var item = new ActivityExecutionHierarchyItem(
            activityExecutionId, workflowExecutionId, "node", "authored", "test.activity", "1",
            ActivityExecutionStatus.Completed, null, 1, Now, Now, Now, null, null, 0, null, null,
            [], 0, 0, 0, null, null, new Dictionary<string, string>());
        return new(workflowExecutionId, activityExecutionId, activityExecutionId, null, 1, item);
    }

    private static BackgroundActivityWorkRequest BackgroundWorkRequest(string workflowExecutionId)
    {
        var attempt = new ActivityAttempt("attempt", "invocation", 1, ActivityAttemptReason.Initial, Now);
        return new BackgroundActivityWorkRequest(
            "background-work", workflowExecutionId, "activity", attempt,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "hash"),
            "node", "contract",
            new ActivityInputSnapshot("invocation", "contract", "binding", new Dictionary<string, ValueEnvelope>(), Now),
            new RuntimeSchedulerWorkItem("coordinator", workflowExecutionId, "command", WorkflowExecutionCommandKind.ScheduleActivity,
                "envelope", "idempotency", Now, Now, 1));
    }

    private sealed class RuntimeFixture : IDisposable
    {
        private readonly ServiceProvider _provider;

        public RuntimeFixture()
        {
            var services = new ServiceCollection();
            services.AddWorkflowRuntime();
            services.AddPersistenceModes();
            _provider = services.BuildServiceProvider();
            var scope = _provider.CreateScope();
            Scope = scope;
            var provider = scope.ServiceProvider;
            DeletionStore = provider.GetRequiredService<IWorkflowExecutionDeletionStore>();
            CheckpointState = provider.GetRequiredService<InMemoryRuntimeCheckpointStoreState>();
            Executions = provider.GetRequiredService<IWorkflowExecutionStateStore>();
            ActivityExecutions = provider.GetRequiredService<IActivityExecutionStateStore>();
            InspectionWriter = provider.GetRequiredService<IActivityExecutionInspectionWriter>();
            Inspections = provider.GetRequiredService<IActivityExecutionInspectionStore>();
            HierarchyWriter = provider.GetRequiredService<IActivityExecutionHierarchyWriter>();
            Hierarchy = (RuntimeInMemoryActivityExecutionHierarchyStore)provider.GetRequiredService<IActivityExecutionHierarchyStore>();
            Bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
            DurableValues = provider.GetRequiredService<IDurableValueStateStore>();
            SchedulerStates = provider.GetRequiredService<ISchedulerStateStore>();
            Timers = provider.GetRequiredService<IDurableTimerStore>();
            SchedulerWork = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            IncidentStates = provider.GetRequiredService<IIncidentStateStore>();
            Poison = provider.GetRequiredService<IWorkflowSchedulerPoisonStore>();
            Holds = provider.GetRequiredService<IWorkflowHoldStateStore>();
            Liveness = provider.GetRequiredService<IExecutionLivenessStateStore>();
        }

        private IServiceScope Scope { get; }
        public IWorkflowExecutionDeletionStore DeletionStore { get; }
        public InMemoryRuntimeCheckpointStoreState CheckpointState { get; }
        public IWorkflowExecutionStateStore Executions { get; }
        public IActivityExecutionStateStore ActivityExecutions { get; }
        public IActivityExecutionInspectionWriter InspectionWriter { get; }
        public IActivityExecutionInspectionStore Inspections { get; }
        public IActivityExecutionHierarchyWriter HierarchyWriter { get; }
        public RuntimeInMemoryActivityExecutionHierarchyStore Hierarchy { get; }
        public IBookmarkStateStore Bookmarks { get; }
        public IDurableValueStateStore DurableValues { get; }
        public ISchedulerStateStore SchedulerStates { get; }
        public IDurableTimerStore Timers { get; }
        public IWorkflowSchedulerWorkQueue SchedulerWork { get; }
        public IIncidentStateStore IncidentStates { get; }
        public IWorkflowSchedulerPoisonStore Poison { get; }
        public IWorkflowHoldStateStore Holds { get; }
        public IExecutionLivenessStateStore Liveness { get; }

        public void Dispose()
        {
            Scope.Dispose();
            _provider.Dispose();
        }
    }
}
