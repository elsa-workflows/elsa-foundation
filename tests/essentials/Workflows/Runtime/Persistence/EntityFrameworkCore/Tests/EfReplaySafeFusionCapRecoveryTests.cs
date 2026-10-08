using System.Collections.Concurrent;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Activities;
using Elsa.Activities.Testing;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Exercises the durable recovery image at a ReplaySafe cap-fold boundary using the real EF SQLite runtime stores.
/// The SQLite backup is a persisted interruption snapshot; it is not an operating-system process-kill test.
/// </summary>
public sealed class EfReplaySafeFusionCapRecoveryTests
{
    private readonly ITestOutputHelper _output;

    private const string WorkflowExecutionId = "wfexec-replaysafe-cap-recovery";
    private const string ActivityExecutionId = "actexec-replaysafe-cap-recovery";
    private const string RecoverySigningKey = "replaysafe-cap-recovery-signing-key-32-bytes";
    private const string HierarchySigningKey = "replaysafe-cap-recovery-hierarchy-signing-key-32-bytes";
    private const string InjectedInnerCheckpointFailureMessage = "Injected inner checkpoint failure after Schedule anchor verification.";

    public EfReplaySafeFusionCapRecoveryTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(CheckpointCommitGatePlacement.InnerStoreReturn)]
    [InlineData(CheckpointCommitGatePlacement.CoalescingDecoratorReturn)]
    public Task ActivityStarted_cap_fold_can_recover_from_a_fresh_provider_without_manual_work_injection(
        CheckpointCommitGatePlacement gatePlacement) =>
        RunCutAndRecoverAsync(
            _output,
            RuntimeCheckpointNames.ActivityStarted,
            maxSegmentCheckpoints: 2,
            NewReplaySafeExecutable,
            expectedActivityStatus: ActivityExecutionStatus.Running,
            gatePlacement: gatePlacement);

    [Theory]
    [InlineData(CheckpointCommitGatePlacement.InnerStoreReturn)]
    [InlineData(CheckpointCommitGatePlacement.CoalescingDecoratorReturn)]
    public Task ActivityAttemptClaimed_cap_one_can_recover_from_a_fresh_provider_without_manual_work_injection(
        CheckpointCommitGatePlacement gatePlacement) =>
        RunCutAndRecoverAsync(
            _output,
            RuntimeCheckpointNames.ActivityAttemptClaimed,
            maxSegmentCheckpoints: 1,
            NewReplaySafeExecutable,
            expectedActivityStatus: ActivityExecutionStatus.Running,
            gatePlacement: gatePlacement);

    [Theory]
    [InlineData(CheckpointCommitGatePlacement.InnerStoreReturn)]
    [InlineData(CheckpointCommitGatePlacement.CoalescingDecoratorReturn)]
    public Task ActivityScheduled_cap_one_can_recover_a_typed_replaysafe_schedule(
        CheckpointCommitGatePlacement gatePlacement) =>
        RunCutAndRecoverAsync(
            _output,
            RuntimeCheckpointNames.ActivityScheduled,
            maxSegmentCheckpoints: 1,
            NewReplaySafeExecutable,
            expectedActivityStatus: ActivityExecutionStatus.Scheduled,
            gatePlacement: gatePlacement);

    [Theory]
    [InlineData(CheckpointCommitGatePlacement.InnerStoreReturn)]
    [InlineData(CheckpointCommitGatePlacement.CoalescingDecoratorReturn)]
    public Task ActivityScheduled_cap_one_can_recover_a_fusable_intrinsic_schedule(
        CheckpointCommitGatePlacement gatePlacement) =>
        RunCutAndRecoverAsync(
            _output,
            RuntimeCheckpointNames.ActivityScheduled,
            maxSegmentCheckpoints: 1,
            NewFusableIntrinsicExecutable,
            expectedActivityStatus: ActivityExecutionStatus.Scheduled,
            expectedOutputName: "result",
            expectedOutputValue: "recovered",
            gatePlacement: gatePlacement);

    [Theory]
    [InlineData(CheckpointCommitGatePlacement.InnerStoreReturn)]
    [InlineData(CheckpointCommitGatePlacement.CoalescingDecoratorReturn)]
    public Task Nested_D2_cap_fold_preserves_fifo_anchors_across_multiple_successors(
        CheckpointCommitGatePlacement gatePlacement) =>
        RunCutAndRecoverAsync(
            _output,
            RuntimeCheckpointNames.ActivityCompleted,
            maxSegmentCheckpoints: 2,
            NewReplaySafeSequenceExecutable,
            expectedActivityStatus: ActivityExecutionStatus.Running,
            expectMultipleActivities: true,
            targetOccurrence: 2,
            enableSequence: true,
            activityExecutionIds: Enumerable.Range(1, 8).Select(index => $"actexec-replaysafe-cap-recovery-{index}").ToArray(),
            recoveryActivityExecutionIds: Enumerable.Range(1, 8).Select(index => $"actexec-replaysafe-cap-recovered-{index}").ToArray(),
            recoveryIdentityNamespace: "replaysafe-cap-recovered",
            gatePlacement: gatePlacement);

    [Fact]
    public async Task Queue_advance_removes_consumed_prefix_before_out_of_order_same_time_continuations()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-replaysafe-cap-order-{Guid.NewGuid():N}.db");
        var completed = false;
        try
        {
            await using var harness = await StartGenerationAsync(databasePath, gate: null);
            await using var scope = harness.Services.CreateAsyncScope();
            var durableQueue = scope.ServiceProvider.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value;
            var queue = new CountingSchedulerWorkQueue(durableQueue);
            var timestamp = WorkflowExecutionHarness.Timestamp;
            await durableQueue.EnqueueAsync(NewQueueWorkItem("seeded-first", 10, timestamp));
            await durableQueue.EnqueueAsync(NewQueueWorkItem("seeded-second", 30, timestamp));

            var session = new RuntimeCoalescingSession(
                WorkflowExecutionId,
                queue,
                new CoalescingRuntimeCheckpointPersistenceOptions());
            await session.EnsureQueueSeededAsync(CancellationToken.None);
            Assert.Equal("seeded-first", (await session.DequeueOverlayAsync(CancellationToken.None))?.WorkItemId);
            await session.EnqueueOverlayAsync(NewQueueWorkItem("continuation-early", 5, timestamp), CancellationToken.None);
            await session.EnqueueOverlayAsync(NewQueueWorkItem("continuation-middle", 20, timestamp), CancellationToken.None);

            // The two continuations have the same timestamp as the original items but sort before and between them
            // by the provider's sequence key. Deleting the consumed seeded prefix after these inserts would dequeue
            // the wrong row and corrupt the tracked durable order.
            await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, CancellationToken.None);
            Assert.Equal(
                new[] { "continuation-early", "continuation-middle", "seeded-second" },
                (await durableQueue.ListAllAsync(WorkflowExecutionId)).Select(item => item.WorkItemId));

            // Subsequent reconciliation must use the provider's actual post-insert order, not overlay insertion order.
            Assert.Equal("seeded-second", (await session.DequeueOverlayAsync(CancellationToken.None))?.WorkItemId);
            await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, CancellationToken.None);
            Assert.Equal(
                new[] { "continuation-early", "continuation-middle", "seeded-second" },
                (await durableQueue.ListAllAsync(WorkflowExecutionId)).Select(item => item.WorkItemId));

            Assert.Equal("continuation-early", (await session.DequeueOverlayAsync(CancellationToken.None))?.WorkItemId);
            Assert.Equal("continuation-middle", (await session.DequeueOverlayAsync(CancellationToken.None))?.WorkItemId);
            await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, CancellationToken.None);
            Assert.Empty(await durableQueue.ListAllAsync(WorkflowExecutionId));

            Assert.Equal(2, queue.EnqueueCalls);
            Assert.Equal(2, queue.ListPageCalls); // one seed page and one refresh after actual inserts
            Assert.Equal(4, queue.DequeueCalls);
            completed = true;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (completed)
                DeleteDatabaseFiles(databasePath);
            else
                Console.Error.WriteLine($"Queue-order regression failed; retained SQLite evidence at '{databasePath}'.");
        }
    }

    [Fact]
    public async Task Under_cap_fused_span_does_not_reenqueue_its_schedule_anchor()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-replaysafe-cap-undercap-{Guid.NewGuid():N}.db");
        var completed = false;
        var operations = new QueueOperationRecorder();
        try
        {
            await using var harness = await StartGenerationAsync(
                databasePath,
                gate: null,
                maxSegmentCheckpoints: 50,
                schedulerQueueDecorator: inner => new CountingSchedulerWorkQueue(inner, operations));
            var run = await harness.RunAsync(NewReplaySafeExecutable());

            Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
            Assert.True(harness.Services.GetRequiredService<RuntimeSchedulerDispatchDiagnostics>().FusedSpans > 0);
            var enqueuedItems = operations.EnqueuedItems.ToArray();
            Assert.Single(enqueuedItems);
            Assert.Equal(WorkflowExecutionCommandKind.Start, enqueuedItems[0].CommandKind);
            Assert.DoesNotContain(enqueuedItems, item => item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity);
            Assert.DoesNotContain(enqueuedItems, item => item.CommandKind is
                WorkflowExecutionCommandKind.StartActivity or WorkflowExecutionCommandKind.InvokeActivity);
            completed = true;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (completed)
                DeleteDatabaseFiles(databasePath);
            else
                Console.Error.WriteLine($"Under-cap queue-write control failed; retained SQLite evidence at '{databasePath}'.");
        }
    }

    [Fact]
    public async Task Later_inner_commit_failure_preserves_anchor_until_scheduler_fault_handling()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-replaysafe-cap-inner-commit-failure-{Guid.NewGuid():N}.db");
        var completed = false;
        var checkpointFailure = new CheckpointCommitFailureProbe();
        try
        {
            await using var harness = await StartGenerationAsync(
                databasePath,
                gate: null,
                maxSegmentCheckpoints: 2,
                innerCheckpointStoreDecorator: (services, inner) => new AnchorVerifiedCheckpointCommitFailureStore(
                    inner,
                    services.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value,
                    checkpointFailure,
                    RuntimeCheckpointNames.ActivityStarted,
                    WorkflowExecutionId,
                    "node-replaysafe-cap"),
                outerCheckpointStoreDecorator: (services, inner) => new CheckpointCommitFailureBoundaryObserverStore(
                    inner,
                    services.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value,
                    services.GetRequiredService<CoalescingInner<IActivityExecutionStateStore>>().Value,
                    checkpointFailure,
                    RuntimeCheckpointNames.ActivityStarted,
                    WorkflowExecutionId,
                    "node-replaysafe-cap",
                    InjectedInnerCheckpointFailureMessage));

            Exception? observedFailure = null;
            WorkflowExecutionStatus? returnedStatus = null;
            try
            {
                returnedStatus = (await harness.RunAsync(NewReplaySafeExecutable())
                    .WaitAsync(TimeSpan.FromSeconds(45))).WorkflowState?.Status;
            }
            catch (Exception exception)
            {
                observedFailure = exception;
            }

            Assert.Equal(1, checkpointFailure.MatchingAttemptCount);
            Assert.True(checkpointFailure.AnchorWasPresentAtAttempt,
                "The original ScheduleActivity must already be durable when the later inner checkpoint write is attempted.");
            Assert.Equal(1, checkpointFailure.FailureUnwindCaptureCount);
            Assert.Null(checkpointFailure.BoundaryObservationFailure);
            Assert.True(checkpointFailure.AnchorWasPresentAfterUnwind,
                "The ScheduleActivity anchor must still be durable after the coalescing commit unwinds and before scheduler fault handling acknowledges it.");
            var statusesAtFailureUnwind = Assert.IsType<ActivityExecutionStatus[]>(checkpointFailure.ActivityStatusesAfterUnwind);
            // The Scheduled state can still be buffered when the first cap flush fails.
            Assert.All(statusesAtFailureUnwind, status => Assert.Equal(ActivityExecutionStatus.Scheduled, status));
            Assert.NotEqual(WorkflowExecutionStatus.Completed, returnedStatus);
            if (observedFailure is not null)
                Assert.Contains(InjectedInnerCheckpointFailureMessage, observedFailure.ToString(), StringComparison.Ordinal);

            await using var scope = harness.Services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var durableQueue = provider.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value;
            var queueItems = await durableQueue.ListAllAsync(WorkflowExecutionId);
            // The drainer acknowledges and poisons a handled checkpoint failure after the observed boundary.
            Assert.Empty(queueItems);
            var poison = Assert.Single(await provider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(WorkflowExecutionId));
            Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, poison.Disposition);
            Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, poison.CommandKind);

            var activities = await provider.GetRequiredService<CoalescingInner<IActivityExecutionStateStore>>().Value
                .ListAllAsync(WorkflowExecutionId);
            Assert.DoesNotContain(activities, activity => activity.Status == ActivityExecutionStatus.Running);
            Assert.DoesNotContain(activities, activity => activity.Status == ActivityExecutionStatus.Completed);
            Assert.All(activities, activity => Assert.Equal(ActivityExecutionStatus.Scheduled, activity.Status));
            completed = true;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (completed)
                DeleteDatabaseFiles(databasePath);
            else
                Console.Error.WriteLine($"Inner-checkpoint failure regression failed; retained SQLite evidence at '{databasePath}'.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fused_anchor_enqueue_failure_or_cancellation_stops_the_continuing_checkpoint(bool cancel)
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-replaysafe-cap-anchor-fault-{Guid.NewGuid():N}.db");
        var completed = false;
        var gate = new CheckpointCommitGate(RuntimeCheckpointNames.ActivityStarted, targetOccurrence: 1);
        var enqueueFault = new AnchorEnqueueFault(cancel);
        try
        {
            await using var harness = await StartGenerationAsync(
                databasePath,
                gate,
                maxSegmentCheckpoints: 2,
                schedulerQueueDecorator: inner => new CountingSchedulerWorkQueue(inner, enqueueFault: enqueueFault));
            Exception? observedFailure = null;
            try
            {
                await harness.RunAsync(NewReplaySafeExecutable()).WaitAsync(TimeSpan.FromSeconds(45));
            }
            catch (Exception exception)
            {
                observedFailure = exception;
            }

            Assert.True(enqueueFault.WasAttempted, "The cap boundary must attempt to persist the active child Schedule anchor.");
            Assert.Equal(1, gate.MatchingAttemptCount);
            Assert.False(gate.WasCaptured, "The checkpoint cannot advance to the durable store when anchor persistence fails or is cancelled.");
            Assert.NotNull(observedFailure);
            if (cancel)
                Assert.Contains("Injected fused-anchor persistence cancellation", observedFailure!.ToString(), StringComparison.Ordinal);
            else
                Assert.Contains("Injected fused-anchor persistence failure", observedFailure!.ToString(), StringComparison.Ordinal);

            var persistedActivities = await harness.Services.GetRequiredService<IActivityExecutionStateStore>()
                .ListAllAsync(WorkflowExecutionId);
            Assert.DoesNotContain(persistedActivities, activity => activity.Status == ActivityExecutionStatus.Running);
            Assert.All(persistedActivities, activity => Assert.Equal(ActivityExecutionStatus.Scheduled, activity.Status));
            completed = true;
        }
        finally
        {
            gate.Release();
            SqliteConnection.ClearAllPools();
            if (completed)
                DeleteDatabaseFiles(databasePath);
            else
                Console.Error.WriteLine($"Anchor persistence failure control failed; retained SQLite evidence at '{databasePath}'.");
        }
    }

    private static async Task RunCutAndRecoverAsync(
        ITestOutputHelper output,
        string checkpointName,
        int maxSegmentCheckpoints,
        Func<WorkflowExecutable> executableFactory,
        ActivityExecutionStatus expectedActivityStatus,
        bool expectMultipleActivities = false,
        int targetOccurrence = 1,
        bool enableSequence = false,
        IReadOnlyCollection<string>? activityExecutionIds = null,
        IReadOnlyCollection<string>? recoveryActivityExecutionIds = null,
        string? recoveryIdentityNamespace = null,
        string? expectedOutputName = null,
        string? expectedOutputValue = null,
        CheckpointCommitGatePlacement gatePlacement = CheckpointCommitGatePlacement.InnerStoreReturn)
    {
        var sourceActivityExecutionIds = (activityExecutionIds ?? [ActivityExecutionId]).ToArray();
        var recoveryIds = (recoveryActivityExecutionIds ?? sourceActivityExecutionIds).ToArray();
        if (recoveryActivityExecutionIds is not null)
            Assert.Empty(sourceActivityExecutionIds.Intersect(recoveryIds, StringComparer.Ordinal));
        Assert.Equal(recoveryActivityExecutionIds is not null, recoveryIdentityNamespace is not null);

        var sourceDatabasePath = Path.Join(Path.GetTempPath(), $"elsa-replaysafe-cap-source-{Guid.NewGuid():N}.db");
        var recoveryDatabasePath = Path.Join(Path.GetTempPath(), $"elsa-replaysafe-cap-recovery-{Guid.NewGuid():N}.db");
        var queueOperations = new QueueOperationRecorder();
        var gate = new CheckpointCommitGate(checkpointName, targetOccurrence, queueOperations);
        var capturedActivityExecutionIds = new HashSet<string>(StringComparer.Ordinal);
        var completed = false;

        try
        {
            await using (var source = await StartGenerationAsync(
                             sourceDatabasePath,
                             gate,
                             maxSegmentCheckpoints: maxSegmentCheckpoints,
                             enableSequence: enableSequence,
                             activityExecutionIds: sourceActivityExecutionIds,
                             gatePlacement: gatePlacement,
                             schedulerQueueDecorator: inner => new CountingSchedulerWorkQueue(inner, queueOperations)))
            {
                var sourceRun = source.RunAsync(executableFactory());
                try
                {
                    var capture = gate.WaitForTargetCheckpointAsync(TimeSpan.FromSeconds(45));
                    if (await Task.WhenAny(capture, sourceRun) != capture)
                    {
                        await sourceRun;
                        throw new InvalidOperationException($"The source run completed before checkpoint '{checkpointName}' was captured.");
                    }

                    var captured = await capture;
                    Assert.Equal(checkpointName, captured.Commit.Checkpoint.Name);
                    if (captured.Claim is { } claimedWork)
                        Assert.Equal(WorkflowExecutionId, claimedWork.WorkflowExecutionId);

                    Assert.True(gate.HasQueueOperationDelta, "The target checkpoint must retain its queue-operation boundary delta.");
                    var queueDelta = gate.QueueOperationDelta;
                    // A prior continuing boundary can already have made the active anchors durable.
                    // Retain the observed deltas, including zero, instead of requiring redundant writes.
                    if (gatePlacement == CheckpointCommitGatePlacement.InnerStoreReturn)
                        Assert.Equal(0, queueDelta.DequeueCalls);
                    output.WriteLine(
                        $"{checkpointName} {gatePlacement}: enqueue={queueDelta.EnqueueCalls}, " +
                        $"ListAsync page calls={queueDelta.ListPageCalls}, dequeue={queueDelta.DequeueCalls}. " +
                        "These are scheduler-queue API/page counts, not SQL command counts.");

                    await using (var scope = source.Services.CreateAsyncScope())
                    {
                        var activities = await scope.ServiceProvider
                            .GetRequiredService<CoalescingInner<IActivityExecutionStateStore>>().Value
                            .ListAllAsync(WorkflowExecutionId);
                        capturedActivityExecutionIds.UnionWith(
                            activities.Select(activity => activity.Execution.ActivityExecutionId));
                        if (expectMultipleActivities)
                        {
                            Assert.True(activities.Count > 1, "The D2 fixture must have parent and successor executions.");
                            Assert.Contains(activities, activity =>
                                activity.Execution.ExecutableNodeId == "node-replaysafe-cap-sequence" &&
                                activity.Status == ActivityExecutionStatus.Running);
                            Assert.Contains(activities, activity =>
                                activity.Execution.ExecutableNodeId == "node-replaysafe-cap-a" &&
                                activity.Status == ActivityExecutionStatus.Completed);
                            Assert.Contains(activities, activity =>
                                activity.Execution.ExecutableNodeId == "node-replaysafe-cap-b" &&
                                activity.Status == ActivityExecutionStatus.Completed);
                            foreach (var activity in activities.Where(activity => activity.Status == ActivityExecutionStatus.Running))
                                Assert.NotNull(activity.InputSnapshot);
                        }
                        else
                        {
                            var activity = Assert.Single(activities);
                            Assert.Equal(expectedActivityStatus, activity.Status);
                            if (expectedActivityStatus == ActivityExecutionStatus.Running)
                                Assert.NotNull(activity.InputSnapshot);
                        }

                        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
                        Assert.NotEmpty(await context.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync());

                        // The captured ScheduleActivity is the only required continuation at these cuts. Its original
                        // durable row must still exist before the checkpoint decorator returns; no pending outbox row
                        // is substituting for the scheduler anchor. The ActivityAttemptClaimed checkpoint can occur
                        // before a consumed scheduler claim is visible to this scoped accessor, so inspect the durable
                        // queue itself instead of relying on the optional claim snapshot.
                        var workflowIdHash = EfRelationalIdentity.Hash(WorkflowExecutionId);
                        var durableQueueRows = await context.SchedulerWorkItems.AsNoTracking()
                            .Where(item => item.WorkflowExecutionIdHash == workflowIdHash)
                            .OrderBy(item => item.WorkOrderKey)
                            .ToArrayAsync();
                        var durableQueue = await scope.ServiceProvider
                            .GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value
                            .ListAllAsync(WorkflowExecutionId);
                        Assert.Equal(
                            durableQueueRows.Select(row => EfRelationalIdentity.Decode(row.WorkItemId)),
                            durableQueue.Select(item => item.WorkItemId));
                        var durableAnchorNodeIds = durableQueue
                            .Where(item => item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity)
                            .Select(ReadScheduleNodeId)
                            .ToArray();
                        if (expectMultipleActivities)
                        {
                            Assert.Equal(
                                new[] { "node-replaysafe-cap-sequence", "node-replaysafe-cap-a", "node-replaysafe-cap-b" },
                                durableAnchorNodeIds);
                        }
                        else
                        {
                            Assert.Single(durableAnchorNodeIds);
                        }
                        var pendingSchedulerOutboxCount = await context.RuntimePostCommitOutbox.AsNoTracking()
                            .CountAsync(item => item.WorkflowExecutionIdHash == workflowIdHash &&
                                                item.IntentKind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork &&
                                                item.Status == (int)RuntimePostCommitOutboxStatus.Pending);
                        Assert.Equal(expectMultipleActivities ? 1 : 0, pendingSchedulerOutboxCount);
                    }

                    await BackupDatabaseAsync(sourceDatabasePath, recoveryDatabasePath);
                }
                finally
                {
                    // Let the original generation finish only on its own database. Recovery uses the earlier backup.
                    gate.Release();
                    try
                    {
                        await sourceRun.WaitAsync(TimeSpan.FromSeconds(45));
                    }
                    catch
                    {
                        // Preserve the capture/assertion failure; a successful capture observes sourceRun below.
                    }
                }

                var original = await sourceRun.WaitAsync(TimeSpan.FromSeconds(45));
                Assert.Equal(WorkflowExecutionStatus.Completed, original.WorkflowState?.Status);
                Assert.True(
                    source.Services.GetRequiredService<RuntimeSchedulerDispatchDiagnostics>().FusedSpans > 0,
                    "The source dispatch must actually enter the ReplaySafe fusion path for this to exercise the cap-fold window.");
                await AssertQueueAndOutboxSettledAsync(source.Services);
                if (expectedOutputName is not null)
                    await AssertWorkflowOutputAsync(source.Services, expectedOutputName, expectedOutputValue!);
            }

            // A fresh provider and normal resumption sweep must find a durable continuation in the copied database.
            // No ScheduleActivity, StartActivity or InvokeActivity item is inserted by the test.
            await using var recovery = await StartGenerationAsync(
                recoveryDatabasePath,
                gate: null,
                timeProvider: new FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(10)),
                maxSegmentCheckpoints: maxSegmentCheckpoints,
                enableSequence: enableSequence,
                activityExecutionIds: recoveryIds,
                executionIdGenerator: recoveryIdentityNamespace is null
                    ? null
                    : new NamespacedRuntimeExecutionIdGenerator(WorkflowExecutionId, recoveryIds, recoveryIdentityNamespace));
            await recovery.SweepUntilQuietAsync();

            await using var recoveryScope = recovery.Services.CreateAsyncScope();
            var recoveredWorkflow = await recoveryScope.ServiceProvider
                .GetRequiredService<IWorkflowExecutionStateStore>()
                .FindAsync(WorkflowExecutionId);
            Assert.NotNull(recoveredWorkflow);
            Assert.Equal(WorkflowExecutionStatus.Completed, recoveredWorkflow!.Status);

            var recoveredActivities = await recoveryScope.ServiceProvider
                .GetRequiredService<IActivityExecutionStateStore>()
                .ListAllAsync(WorkflowExecutionId);
            if (recoveryActivityExecutionIds is not null)
            {
                var recoveredIds = recoveredActivities.Select(activity => activity.Execution.ActivityExecutionId).ToArray();
                var newlyGeneratedRecoveryIds = recoveredIds
                    .Where(activityExecutionId => !capturedActivityExecutionIds.Contains(activityExecutionId))
                    .ToArray();
                Assert.NotEmpty(newlyGeneratedRecoveryIds);
                Assert.All(newlyGeneratedRecoveryIds, activityExecutionId => Assert.Contains(activityExecutionId, recoveryIds));
                Assert.Empty(capturedActivityExecutionIds.Intersect(newlyGeneratedRecoveryIds, StringComparer.Ordinal));
            }
            if (expectMultipleActivities)
            {
                Assert.Equal(4, recoveredActivities.Count);
                Assert.Equal(
                    new[]
                    {
                        "node-replaysafe-cap-a",
                        "node-replaysafe-cap-b",
                        "node-replaysafe-cap-c",
                        "node-replaysafe-cap-sequence"
                    },
                    recoveredActivities.Select(activity => activity.Execution.ExecutableNodeId).Order(StringComparer.Ordinal));
                Assert.All(recoveredActivities, activity => Assert.Equal(ActivityExecutionStatus.Completed, activity.Status));
            }
            else
            {
                var recoveredActivity = Assert.Single(recoveredActivities);
                Assert.Equal(ActivityExecutionStatus.Completed, recoveredActivity.Status);
            }
            Assert.Empty(await recoveryScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                .ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
            await AssertQueueAndOutboxSettledAsync(recovery.Services);
            if (expectedOutputName is not null)
                await AssertWorkflowOutputAsync(recovery.Services, expectedOutputName, expectedOutputValue!);
            completed = true;
        }
        finally
        {
            gate.Release();
            SqliteConnection.ClearAllPools();
            if (completed)
            {
                DeleteDatabaseFiles(sourceDatabasePath);
                DeleteDatabaseFiles(recoveryDatabasePath);
            }
            else
            {
                Console.Error.WriteLine(
                    $"Cap-fold recovery test failed; retained SQLite evidence at '{sourceDatabasePath}' and '{recoveryDatabasePath}'.");
            }
        }
    }

    private static async Task<WorkflowExecutionHarness> StartGenerationAsync(
        string databasePath,
        CheckpointCommitGate? gate,
        TimeProvider? timeProvider = null,
        int maxSegmentCheckpoints = 2,
        bool enableSequence = false,
        IReadOnlyCollection<string>? activityExecutionIds = null,
        IRuntimeExecutionIdGenerator? executionIdGenerator = null,
        Func<IWorkflowSchedulerWorkQueue, IWorkflowSchedulerWorkQueue>? schedulerQueueDecorator = null,
        CheckpointCommitGatePlacement gatePlacement = CheckpointCommitGatePlacement.InnerStoreReturn,
        Func<IServiceProvider, IRuntimeCheckpointCommitStore, IRuntimeCheckpointCommitStore>? innerCheckpointStoreDecorator = null,
        Func<IServiceProvider, IRuntimeCheckpointCommitStore, IRuntimeCheckpointCommitStore>? outerCheckpointStoreDecorator = null)
    {
        var builder = WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={databasePath};Pooling=False",
                    RecoveryContinuationSigningKey = RecoverySigningKey,
                    HierarchyCursorSigningKey = HierarchySigningKey
                });
                services.AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                if (timeProvider is not null)
                    services.AddSingleton(timeProvider);
            })
            .WithCoalescing(maxSegmentCheckpoints: maxSegmentCheckpoints);

        if (enableSequence)
            builder = builder.WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services));

        if (gate is not null)
            builder = builder.ConfigureServices(services => AddCheckpointCommitGate(services, gate, gatePlacement));
        if (executionIdGenerator is not null)
            builder = builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton(executionIdGenerator)));
        if (schedulerQueueDecorator is not null)
            builder = builder.ConfigureServices(services => AddSchedulerQueueDecorator(services, schedulerQueueDecorator));
        if (innerCheckpointStoreDecorator is not null)
            builder = builder.ConfigureServices(services => AddInnerCheckpointStoreDecorator(services, innerCheckpointStoreDecorator));
        if (outerCheckpointStoreDecorator is not null)
            builder = builder.ConfigureServices(services => AddOuterCheckpointStoreDecorator(services, outerCheckpointStoreDecorator));

        var harness = builder.Build(
            WorkflowExecutionHarness.Identity,
            WorkflowExecutionId,
            activityExecutionIds ?? [ActivityExecutionId]);

        var initializers = harness.Services.GetServices<IShellInitializer>().ToArray();
        Assert.Contains(initializers, initializer => initializer is EfModuleMigrator<RuntimeDbContext>);
        foreach (var initializer in initializers)
            await initializer.InitializeAsync();
        harness.InitializeActivityTypes();
        return harness;
    }

    private static void AddSchedulerQueueDecorator(
        IServiceCollection services,
        Func<IWorkflowSchedulerWorkQueue, IWorkflowSchedulerWorkQueue> decorate)
        => AddCoalescingInnerDecorator<IWorkflowSchedulerWorkQueue>(services, (_, inner) => decorate(inner));

    private static void AddInnerCheckpointStoreDecorator(
        IServiceCollection services,
        Func<IServiceProvider, IRuntimeCheckpointCommitStore, IRuntimeCheckpointCommitStore> decorate)
        => AddCoalescingInnerDecorator(services, decorate);

    private static void AddOuterCheckpointStoreDecorator(
        IServiceCollection services,
        Func<IServiceProvider, IRuntimeCheckpointCommitStore, IRuntimeCheckpointCommitStore> decorate)
    {
        var registered = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore))
            ?? throw new InvalidOperationException("The test observer could not find the coalescing checkpoint-store registration.");
        if (registered.ImplementationFactory is null)
            throw new InvalidOperationException("The test observer requires the coalescing checkpoint-store factory registration.");

        services.Remove(registered);
        services.Add(new ServiceDescriptor(
            typeof(IRuntimeCheckpointCommitStore),
            serviceProvider => decorate(
                serviceProvider,
                (IRuntimeCheckpointCommitStore)registered.ImplementationFactory(serviceProvider)),
            registered.Lifetime));
    }

    private static void AddCoalescingInnerDecorator<T>(
        IServiceCollection services,
        Func<IServiceProvider, T, T> decorate)
        where T : class
    {
        var serviceType = typeof(CoalescingInner<T>);
        var registered = services.LastOrDefault(descriptor => descriptor.ServiceType == serviceType)
            ?? throw new InvalidOperationException($"The test decorator could not find the durable {typeof(T).Name} registration captured by coalescing.");
        if (registered.ImplementationFactory is null)
            throw new InvalidOperationException($"The test decorator requires the durable {typeof(T).Name} factory registration captured by coalescing.");

        services.Remove(registered);
        services.Add(new ServiceDescriptor(
            serviceType,
            serviceProvider =>
            {
                var inner = (CoalescingInner<T>)registered.ImplementationFactory(serviceProvider);
                return new CoalescingInner<T>(decorate(serviceProvider, inner.Value));
            },
            registered.Lifetime));
    }

    private static void AddCheckpointCommitGate(
        IServiceCollection services,
        CheckpointCommitGate gate,
        CheckpointCommitGatePlacement placement)
    {
        if (placement == CheckpointCommitGatePlacement.InnerStoreReturn)
        {
            // Freeze the durable commit after it lands but before the coalescing decorator advances queue rows.
            var registered = services.LastOrDefault(descriptor =>
                    descriptor.ServiceType == typeof(CoalescingInner<IRuntimeCheckpointCommitStore>))
                ?? throw new InvalidOperationException("The test gate could not find the durable checkpoint-store registration captured by coalescing.");
            if (registered.ImplementationFactory is null)
                throw new InvalidOperationException("The test gate requires the durable checkpoint-store factory registration captured by coalescing.");

            services.Remove(registered);
            services.Add(new ServiceDescriptor(
                typeof(CoalescingInner<IRuntimeCheckpointCommitStore>),
                serviceProvider =>
                {
                    var inner = (CoalescingInner<IRuntimeCheckpointCommitStore>)registered.ImplementationFactory(serviceProvider);
                    return new CoalescingInner<IRuntimeCheckpointCommitStore>(CreateCheckpointCommitGateStore(
                        serviceProvider,
                        inner.Value,
                        gate,
                        recordAttempt: false));
                },
                registered.Lifetime));
            AddCheckpointCommitAttemptRecorder(services, gate);
            return;
        }

        // Freeze after the coalescing decorator has also advanced its queue and returned to its caller.
        var decorated = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore))
            ?? throw new InvalidOperationException("The test gate could not find the coalescing checkpoint-store registration.");
        if (decorated.ImplementationFactory is null)
            throw new InvalidOperationException("The test gate requires the coalescing checkpoint-store factory registration.");

        services.Remove(decorated);
        services.Add(new ServiceDescriptor(
            typeof(IRuntimeCheckpointCommitStore),
            serviceProvider => CreateCheckpointCommitGateStore(
                serviceProvider,
                (IRuntimeCheckpointCommitStore)decorated.ImplementationFactory(serviceProvider),
                gate,
                recordAttempt: true),
            decorated.Lifetime));
    }

    private static void AddCheckpointCommitAttemptRecorder(IServiceCollection services, CheckpointCommitGate gate)
    {
        var decorated = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore))
            ?? throw new InvalidOperationException("The test observer could not find the coalescing checkpoint-store registration.");
        if (decorated.ImplementationFactory is null)
            throw new InvalidOperationException("The test observer requires the coalescing checkpoint-store factory registration.");

        services.Remove(decorated);
        services.Add(new ServiceDescriptor(
            typeof(IRuntimeCheckpointCommitStore),
            serviceProvider => new CheckpointCommitAttemptRecorderStore(
                (IRuntimeCheckpointCommitStore)decorated.ImplementationFactory(serviceProvider),
                gate),
            decorated.Lifetime));
    }

    private static CheckpointCommitGateStore CreateCheckpointCommitGateStore(
        IServiceProvider serviceProvider,
        IRuntimeCheckpointCommitStore inner,
        CheckpointCommitGate gate,
        bool recordAttempt) =>
        new(inner, gate, serviceProvider.GetRequiredService<IRuntimeConsumedSchedulerWorkClaimAccessor>(), recordAttempt);

    private static WorkflowExecutable NewReplaySafeExecutable() =>
        WorkflowExecutionHarness.NewExecutable(
            WorkflowExecutionHarness.NewReplaySafeProbeNode("node-replaysafe-cap"));

    private static WorkflowExecutable NewReplaySafeSequenceExecutable()
    {
        var children = new[]
        {
            WorkflowExecutionHarness.NewReplaySafeProbeNode("node-replaysafe-cap-a"),
            WorkflowExecutionHarness.NewReplaySafeProbeNode("node-replaysafe-cap-b"),
            WorkflowExecutionHarness.NewReplaySafeProbeNode("node-replaysafe-cap-c")
        };
        var sequenceContract = ClrActivityContractTestBuilder.BuildContract(typeof(SequenceActivity));
        var root = new ExecutableNode(
            executableNodeId: "node-replaysafe-cap-sequence",
            authoredActivityId: "authored-replaysafe-cap-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: sequenceContract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray() })),
            activityContract: sequenceContract);
        return new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutable NewFusableIntrinsicExecutable()
    {
        var stringType = new ValueTypeDescriptor("String");
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [WorkflowIntrinsicInputKeys.Name] = new(
                WorkflowIntrinsicInputKeys.Name,
                stringType,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(
                    stringType,
                    JsonSerializer.SerializeToElement("result"),
                    ValueProtectionPolicy.InstanceInline)),
            [WorkflowIntrinsicInputKeys.Value] = new(
                WorkflowIntrinsicInputKeys.Value,
                stringType,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(
                    stringType,
                    JsonSerializer.SerializeToElement("recovered"),
                    ValueProtectionPolicy.InstanceInline))
        };
        var node = new ExecutableNode(
            executableNodeId: "node-set-output-cap",
            authoredActivityId: "authored-set-output-cap",
            activityType: "elsa.intrinsic.setoutput",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { kind = "SetOutput", schemaVersion = "1.0.0" }),
            inputBindings: bindings,
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.SetOutput);
        return WorkflowExecutionHarness.NewExecutable(node);
    }

    private static string ReadScheduleNodeId(RuntimeSchedulerWorkItem workItem)
    {
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, workItem.CommandKind);
        var payload = workItem.Payload?.Deserialize<RuntimeScheduleActivityCommandPayload>();
        Assert.NotNull(payload);
        return payload!.ExecutableNodeId;
    }

    private static RuntimeSchedulerWorkItem NewQueueWorkItem(string workItemId, long sequence, DateTimeOffset timestamp) =>
        new(
            workItemId,
            WorkflowExecutionId,
            $"command-{workItemId}",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            $"envelope-{workItemId}",
            $"{WorkflowExecutionId}:{workItemId}",
            timestamp,
            timestamp,
            sequence,
            JsonSerializer.SerializeToElement(new { workItemId }));

    private sealed class QueueOperationRecorder
    {
        private int _enqueueCalls;
        private int _listPageCalls;
        private int _dequeueCalls;

        public ConcurrentQueue<RuntimeSchedulerWorkItem> EnqueuedItems { get; } = new();

        public QueueOperationCounts Snapshot() => new(
            Volatile.Read(ref _enqueueCalls),
            Volatile.Read(ref _listPageCalls),
            Volatile.Read(ref _dequeueCalls));

        public void RecordEnqueue(RuntimeSchedulerWorkItem workItem)
        {
            Interlocked.Increment(ref _enqueueCalls);
            EnqueuedItems.Enqueue(workItem);
        }

        public void RecordListPage() => Interlocked.Increment(ref _listPageCalls);

        public void RecordDequeue() => Interlocked.Increment(ref _dequeueCalls);
    }

    private readonly record struct QueueOperationCounts(int EnqueueCalls, int ListPageCalls, int DequeueCalls)
    {
        public QueueOperationCounts DifferenceFrom(QueueOperationCounts earlier) => new(
            EnqueueCalls - earlier.EnqueueCalls,
            ListPageCalls - earlier.ListPageCalls,
            DequeueCalls - earlier.DequeueCalls);
    }

    private sealed class AnchorEnqueueFault(bool cancel)
    {
        private int _wasAttempted;

        public bool WasAttempted => Volatile.Read(ref _wasAttempted) != 0;

        public Exception? TryCreateException(RuntimeSchedulerWorkItem workItem)
        {
            if (workItem.CommandKind != WorkflowExecutionCommandKind.ScheduleActivity)
                return null;

            Interlocked.Exchange(ref _wasAttempted, 1);
            return cancel
                ? new OperationCanceledException("Injected fused-anchor persistence cancellation.", new CancellationToken(canceled: true))
                : new InvalidOperationException("Injected fused-anchor persistence failure.");
        }
    }

    private sealed class CheckpointCommitFailureProbe
    {
        private int _matchingAttemptCount;
        private int _anchorWasPresentAtAttempt;
        private int _failureUnwindCaptureCount;
        private int _anchorWasPresentAfterUnwind;
        private ActivityExecutionStatus[]? _activityStatusesAfterUnwind;
        private string? _boundaryObservationFailure;

        public int MatchingAttemptCount => Volatile.Read(ref _matchingAttemptCount);
        public bool AnchorWasPresentAtAttempt => Volatile.Read(ref _anchorWasPresentAtAttempt) != 0;
        public int FailureUnwindCaptureCount => Volatile.Read(ref _failureUnwindCaptureCount);
        public bool AnchorWasPresentAfterUnwind => Volatile.Read(ref _anchorWasPresentAfterUnwind) != 0;
        public ActivityExecutionStatus[]? ActivityStatusesAfterUnwind => Volatile.Read(ref _activityStatusesAfterUnwind);
        public string? BoundaryObservationFailure => Volatile.Read(ref _boundaryObservationFailure);

        public void Record(bool anchorWasPresent)
        {
            if (anchorWasPresent)
                Volatile.Write(ref _anchorWasPresentAtAttempt, 1);
            Interlocked.Increment(ref _matchingAttemptCount);
        }

        public void RecordFailureUnwind(bool anchorWasPresent, IEnumerable<ActivityExecutionStatus> activityStatuses)
        {
            if (anchorWasPresent)
                Volatile.Write(ref _anchorWasPresentAfterUnwind, 1);
            Volatile.Write(ref _activityStatusesAfterUnwind, activityStatuses.ToArray());
            Interlocked.Increment(ref _failureUnwindCaptureCount);
        }

        public void RecordBoundaryObservationFailure(Exception exception) =>
            Volatile.Write(ref _boundaryObservationFailure, exception.ToString());
    }

    private sealed class AnchorVerifiedCheckpointCommitFailureStore(
        IRuntimeCheckpointCommitStore inner,
        IWorkflowSchedulerWorkQueue durableQueue,
        CheckpointCommitFailureProbe probe,
        string checkpointName,
        string workflowExecutionId,
        string expectedAnchorNodeId) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            if (StringComparer.Ordinal.Equals(commit.Checkpoint.Name, checkpointName))
            {
                var durableItems = await durableQueue.ListAllAsync(workflowExecutionId, cancellationToken);
                var anchorWasPresent = durableItems.Any(item =>
                    item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity &&
                    StringComparer.Ordinal.Equals(ReadScheduleNodeId(item), expectedAnchorNodeId));
                probe.Record(anchorWasPresent);
                throw new InvalidOperationException(InjectedInnerCheckpointFailureMessage);
            }

            return await inner.CommitAsync(commit, decision, cancellationToken);
        }
    }

    private sealed class CheckpointCommitFailureBoundaryObserverStore(
        IRuntimeCheckpointCommitStore inner,
        IWorkflowSchedulerWorkQueue durableQueue,
        IActivityExecutionStateStore durableActivityStateStore,
        CheckpointCommitFailureProbe probe,
        string checkpointName,
        string workflowExecutionId,
        string expectedAnchorNodeId,
        string expectedFailureMessage) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await inner.CommitAsync(commit, decision, cancellationToken);
            }
            catch (Exception exception) when (
                StringComparer.Ordinal.Equals(commit.Checkpoint.Name, checkpointName) &&
                exception.Message.Contains(expectedFailureMessage, StringComparison.Ordinal))
            {
                try
                {
                    // This wrapper is outside coalescing, so the capture precedes drainer fault/poison handling.
                    var durableItems = await durableQueue.ListAllAsync(workflowExecutionId, CancellationToken.None);
                    var activities = await durableActivityStateStore.ListAllAsync(workflowExecutionId, CancellationToken.None);
                    var anchorWasPresent = durableItems.Any(item =>
                        item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity &&
                        StringComparer.Ordinal.Equals(ReadScheduleNodeId(item), expectedAnchorNodeId));
                    probe.RecordFailureUnwind(anchorWasPresent, activities.Select(activity => activity.Status));
                }
                catch (Exception observationException)
                {
                    // Preserve the injected commit failure; the test asserts that the boundary capture succeeded.
                    probe.RecordBoundaryObservationFailure(observationException);
                }

                throw;
            }
        }
    }

    private sealed class CountingSchedulerWorkQueue(
        IWorkflowSchedulerWorkQueue inner,
        QueueOperationRecorder? recorder = null,
        AnchorEnqueueFault? enqueueFault = null) : IWorkflowSchedulerWorkQueue
    {
        public int EnqueueCalls { get; private set; }
        public int ListPageCalls { get; private set; }
        public int DequeueCalls { get; private set; }

        public bool SupportsClaimTransitions => inner.SupportsClaimTransitions;
        public bool SupportsClaimableBacklogDiscovery => inner.SupportsClaimableBacklogDiscovery;

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            EnqueueCalls++;
            recorder?.RecordEnqueue(workItem);
            if (enqueueFault?.TryCreateException(workItem) is { } exception)
                throw exception;
            return inner.EnqueueAsync(workItem, cancellationToken);
        }

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default)
        {
            ListPageCalls++;
            recorder?.RecordListPage();
            return inner.ListAsync(query, cancellationToken);
        }

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            DequeueCalls++;
            recorder?.RecordDequeue();
            return inner.DequeueAsync(workflowExecutionId, cancellationToken);
        }

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListClaimableWorkflowExecutionIdsAsync(
            RuntimeSchedulerClaimableBacklogQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListClaimableWorkflowExecutionIdsAsync(query, cancellationToken);

        public ValueTask<IReadOnlyDictionary<string, RuntimeSchedulerWorkItem>> ListNextWorkItemsAsync(
            IReadOnlyCollection<string> workflowExecutionIds,
            CancellationToken cancellationToken = default) =>
            inner.ListNextWorkItemsAsync(workflowExecutionIds, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
            RuntimeSchedulerWorkClaimRequest request,
            CancellationToken cancellationToken = default) =>
            inner.ClaimAsync(request, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(
            RuntimeSchedulerWorkClaim claim,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            CancellationToken cancellationToken = default) =>
            inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(
            RuntimeSchedulerWorkClaim claim,
            CancellationToken cancellationToken = default) =>
            inner.CompleteClaimAsync(claim, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(
            RuntimeSchedulerWorkClaim claim,
            DateTimeOffset visibleAt,
            CancellationToken cancellationToken = default) =>
            inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(
            ConsumedSchedulerWorkItem consumed,
            CancellationToken cancellationToken = default) =>
            inner.ConsumeClaimedAsync(consumed, cancellationToken);
    }

    public enum CheckpointCommitGatePlacement
    {
        InnerStoreReturn,
        CoalescingDecoratorReturn
    }

    private sealed class NamespacedRuntimeExecutionIdGenerator(
        string workflowExecutionId,
        IEnumerable<string> activityExecutionIds,
        string namespacePrefix) : IRuntimeExecutionIdGenerator
    {
        private readonly ConcurrentQueue<string> _activityExecutionIds = new(activityExecutionIds);
        private int _commandOrdinal;
        private int _envelopeOrdinal;

        public string NewWorkflowExecutionId() => workflowExecutionId;

        public string NewWorkflowExecutionCommandId() => $"{namespacePrefix}-command-{Interlocked.Increment(ref _commandOrdinal)}";

        public string NewWorkflowExecutionCommandEnvelopeId() => $"{namespacePrefix}-envelope-{Interlocked.Increment(ref _envelopeOrdinal)}";

        public string NewActivityExecutionId() =>
            _activityExecutionIds.TryDequeue(out var activityExecutionId)
                ? activityExecutionId
                : throw new InvalidOperationException("No namespaced recovery activity execution ID is available.");
    }

    private static async Task AssertQueueAndOutboxSettledAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var queue = provider.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value;
        Assert.Empty(await queue.ListAllAsync(WorkflowExecutionId));
        Assert.Empty(await provider.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionId));

        var context = provider.GetRequiredService<RuntimeDbContext>();
        var workflowIdHash = EfRelationalIdentity.Hash(WorkflowExecutionId);
        var unsettledOutbox = await context.RuntimePostCommitOutbox.AsNoTracking()
            .Where(item => item.WorkflowExecutionIdHash == workflowIdHash &&
                           item.Status != (int)RuntimePostCommitOutboxStatus.Delivered &&
                           item.Status != (int)RuntimePostCommitOutboxStatus.Cancelled)
            .Select(item => new { item.IntentKind, item.Status })
            .ToArrayAsync();
        Assert.Empty(unsettledOutbox);
        Assert.Empty(await context.WorkflowSchedulerPoisonRecords.AsNoTracking()
            .Where(item => item.WorkflowExecutionIdHash == workflowIdHash)
            .ToArrayAsync());
    }

    private static async Task AssertWorkflowOutputAsync(IServiceProvider services, string name, string expectedValue)
    {
        var values = await services.GetRequiredService<IDurableValueStateStore>()
            .ListAllDurableValueStatesAsync(WorkflowExecutionId);
        var output = Assert.Single(
            RuntimeWorkflowOutputStateProjection.Project(values, services.GetRequiredService<IRuntimePayloadCapturePolicy>()),
            projection => projection.Name == name);
        var actualValue = output.Value switch
        {
            { ValueKind: JsonValueKind.Object } snapshot when snapshot.TryGetProperty("preview", out var preview) => preview.GetString(),
            { ValueKind: JsonValueKind.String } inline => inline.GetString(),
            _ => null
        };
        Assert.Equal(expectedValue, actualValue);
    }

    private static async Task BackupDatabaseAsync(string sourcePath, string destinationPath)
    {
        await using var source = new SqliteConnection($"Data Source={sourcePath};Pooling=False");
        await using var destination = new SqliteConnection($"Data Source={destinationPath};Pooling=False");
        await source.OpenAsync();
        await destination.OpenAsync();
        source.BackupDatabase(destination);
        Assert.True(File.Exists(destinationPath));
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    private sealed class CheckpointCommitGate(
        string checkpointName,
        int targetOccurrence,
        QueueOperationRecorder? queueOperations = null)
    {
        private readonly TaskCompletionSource<(RuntimeCheckpointCommit Commit, ConsumedSchedulerWorkItem? Claim)> _captured =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;
        private int _matchingCheckpoints;
        private int _matchingAttempts;
        private QueueOperationCounts? _queueOperationsBeforeTarget;
        private QueueOperationCounts _queueOperationDelta;
        private int _hasQueueOperationDelta;

        public Task<(RuntimeCheckpointCommit Commit, ConsumedSchedulerWorkItem? Claim)> WaitForTargetCheckpointAsync(TimeSpan timeout) =>
            _captured.Task.WaitAsync(timeout);

        public bool WasCaptured => _captured.Task.IsCompletedSuccessfully;
        public int MatchingAttemptCount => Volatile.Read(ref _matchingAttempts);
        public bool HasQueueOperationDelta => Volatile.Read(ref _hasQueueOperationDelta) != 0;
        public QueueOperationCounts QueueOperationDelta => _queueOperationDelta;

        public void Release() => _release.TrySetResult();

        public void RecordAttempt(RuntimeCheckpointCommit commit)
        {
            if (StringComparer.Ordinal.Equals(commit.Checkpoint.Name, checkpointName))
            {
                var matchingAttempt = Interlocked.Increment(ref _matchingAttempts);
                if (matchingAttempt == targetOccurrence && queueOperations is not null)
                    _queueOperationsBeforeTarget = queueOperations.Snapshot();
            }
        }

        public async ValueTask PauseAfterCommitAsync(
            RuntimeCheckpointCommit commit,
            ConsumedSchedulerWorkItem? claim,
            CancellationToken cancellationToken)
        {
            if (!StringComparer.Ordinal.Equals(commit.Checkpoint.Name, checkpointName) ||
                Interlocked.Increment(ref _matchingCheckpoints) != targetOccurrence ||
                Interlocked.CompareExchange(ref _entered, 1, 0) != 0)
                return;

            if (queueOperations is not null && _queueOperationsBeforeTarget is { } before)
            {
                _queueOperationDelta = queueOperations.Snapshot().DifferenceFrom(before);
                Volatile.Write(ref _hasQueueOperationDelta, 1);
            }

            _captured.TrySetResult((commit, claim));
            await _release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CheckpointCommitGateStore(
        IRuntimeCheckpointCommitStore inner,
        CheckpointCommitGate gate,
        IRuntimeConsumedSchedulerWorkClaimAccessor consumedWorkClaimAccessor,
        bool recordAttempt) : IRuntimeCheckpointCommitStore
    {
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            if (recordAttempt)
                gate.RecordAttempt(commit);
            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            await gate.PauseAfterCommitAsync(commit, consumedWorkClaimAccessor.PendingConsume, cancellationToken);
            return result;
        }
    }

    private sealed class CheckpointCommitAttemptRecorderStore(
        IRuntimeCheckpointCommitStore inner,
        CheckpointCommitGate gate) : IRuntimeCheckpointCommitStore
    {
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            gate.RecordAttempt(commit);
            return inner.CommitAsync(commit, decision, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
