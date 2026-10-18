using System.Collections.Concurrent;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Exercises opt-in background leaves through the published native Sequence/Flowchart path and the Runtime EF
/// aggregate. Workers are driven through the public claim/execute API so their activity bodies can be held at a
/// deterministic barrier while the workflow coordinator remains independently available.
/// </summary>
public sealed class NativeBackgroundActivityFlowchartEfTests : IDisposable
{
    private const string ExecutionIdPrefix = "native-background-ef-";
    private const string SourceReferenceId = "source-native-background-ef";
    private const string FlowchartNodeId = "flowchart";
    private const string SequenceNodeId = "sequence";
    private const string ForkNodeId = "fork";
    private const string LeftNodeId = "background-left";
    private const string RightNodeId = "background-right";
    private const string JoinNodeId = "join";
    private const string FinalNodeId = "final";
    private const string LeftVariableKey = "var-left-result";
    private const string RightVariableKey = "var-right-result";
    private const string RecoverySigningKey = "native-background-ef-recovery-signing-key-32";
    private const string HierarchySigningKey = "native-background-ef-hierarchy-signing-key-32";

    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-native-background-ef-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData(LeftNodeId, PersistenceMode.Durable)]
    [InlineData(RightNodeId, PersistenceMode.Durable)]
    [InlineData(LeftNodeId, PersistenceMode.Balanced)]
    [InlineData(RightNodeId, PersistenceMode.Balanced)]
    public async Task Published_native_sequence_waits_for_both_background_results_in_either_order(string firstNodeId, PersistenceMode persistenceMode)
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var barrier = new BackgroundBodyBarrier();
        var finalCounter = new FinalStepCounter();
        await using var harness = await StartHarnessAsync(executionId, barrier, finalCounter, persistenceMode);
        var reference = await PublishAsync(harness);
        await CopyPublicationIntoPartitionAsync(harness, reference);

        var started = await harness.StartPublishedAsync(reference, executionId, correlationId: "background-correlation");
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, started.CommandDispatch.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(harness, executionId));
        await using (var scope = await CreatePartitionScopeAsync(harness))
        {
            var admitted = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId);
            Assert.Equal("true", admitted!.SystemMetadata[RuntimeMetadataKeys.WorkflowHasBackgroundActivityWork]);
            Assert.Equal(SourceReferenceId, admitted.SystemMetadata[RuntimeMetadataKeys.SourceReferenceId]);
            Assert.Equal("background-correlation", admitted.CorrelationId);
        }

        var workers = await ClaimTwoAsync(harness, executionId);
        Assert.Equal(2, workers.Count);
        Assert.Equal(new[] { LeftNodeId, RightNodeId }, workers.Select(item => item.Request.ExecutableNodeId).Order().ToArray());

        var worker = harness.Services.GetRequiredService<BackgroundActivityWorker>();
        var left = workers.Single(item => item.Request.ExecutableNodeId == LeftNodeId);
        var right = workers.Single(item => item.Request.ExecutableNodeId == RightNodeId);
        var workerScope = new PersistenceScope(WorkflowExecutionHarness.Partition.Value);
        var leftRun = worker.ExecuteClaimAsync(workerScope, left.Claim);
        var rightRun = worker.ExecuteClaimAsync(workerScope, right.Claim);

        // Both independently scoped worker bodies must have entered before either is released.
        await Task.WhenAll(
            barrier.WaitUntilEnteredAsync("left", TimeSpan.FromSeconds(10)),
            barrier.WaitUntilEnteredAsync("right", TimeSpan.FromSeconds(10)));

        var first = firstNodeId == LeftNodeId ? left : right;
        var second = firstNodeId == LeftNodeId ? right : left;
        var firstTask = firstNodeId == LeftNodeId ? leftRun : rightRun;
        var secondTask = firstNodeId == LeftNodeId ? rightRun : leftRun;

        barrier.Release(first.Request.ExecutableNodeId == LeftNodeId ? "left" : "right");
        await firstTask.WaitAsync(TimeSpan.FromSeconds(10));
        await DrivePartitionAsync(harness, executionId);

        Assert.Equal(0, finalCounter.Count);
        Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(harness, executionId));
        Assert.Equal(ActivityExecutionStatus.Completed, await ActivityStatusAsync(harness, executionId, first.Request.ExecutableNodeId));
        Assert.Equal(ActivityExecutionStatus.Running, await ActivityStatusAsync(harness, executionId, second.Request.ExecutableNodeId));

        barrier.Release(second.Request.ExecutableNodeId == LeftNodeId ? "left" : "right");
        await secondTask.WaitAsync(TimeSpan.FromSeconds(10));
        await DrivePartitionAsync(harness, executionId);

        var workflow = await WorkflowStateAsync(harness, executionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflow!.Status);
        Assert.Equal("left-result", workflow.RootVariableFrame!.Values[LeftVariableKey].InlineValue!.Value.GetString());
        Assert.Equal("right-result", workflow.RootVariableFrame.Values[RightVariableKey].InlineValue!.Value.GetString());
        Assert.Equal(1, finalCounter.Count);

        var states = await ActivityStatesAsync(harness, executionId);
        Assert.Single(states, state => state.Execution.ExecutableNodeId == SequenceNodeId && state.Status == ActivityExecutionStatus.Completed);
        Assert.Single(states, state => state.Execution.ExecutableNodeId == FlowchartNodeId && state.Status == ActivityExecutionStatus.Completed);
        Assert.Single(states, state => state.Execution.ExecutableNodeId == FinalNodeId && state.Status == ActivityExecutionStatus.Completed);

        // A repeated saved coordinator pointer after acceptance may be delivered, but the completed native cascade
        // remains terminal and the Sequence successor is not invoked a second time.
        await using (var scope = await CreatePartitionScopeAsync(harness))
        {
            var accepted = await scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>()
                .FindAsync(executionId, first.Request.WorkId);
            Assert.Equal(BackgroundActivityWorkStatus.Accepted, accepted!.Status);
            await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                .EnqueueAsync(first.Request.CoordinatorWorkItem);
        }

        await DrivePartitionAsync(harness, executionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, await WorkflowStatusAsync(harness, executionId));
        Assert.Equal(1, finalCounter.Count);
        Assert.Single(await ActivityStatesAsync(harness, executionId), state => state.Execution.ExecutableNodeId == FinalNodeId);

        WorkflowExecutionStatePage completedHistory;
        IReadOnlyDictionary<string, ActivityExecutionStatus> completedActivities;
        await using (var scope = await CreatePartitionScopeAsync(harness))
        {
            var store = scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>();
            foreach (var item in workers)
                Assert.Equal(BackgroundActivityWorkStatus.Accepted,
                    (await store.FindAsync(executionId, item.Request.WorkId))!.Status);
            completedHistory = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>()
                .QueryPageAsync(new WorkflowExecutionStatePageQuery(10, WorkflowExecutionId: executionId));
            completedActivities = (await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>()
                    .ListAllAsync(executionId))
                .ToDictionary(state => state.Execution.ActivityExecutionId, state => state.Status, StringComparer.Ordinal);
        }

        Assert.Single(completedHistory.Items);
        Assert.Equal(WorkflowExecutionStatus.Completed, completedHistory.Items[0].Status);
        Assert.Equal("true", completedHistory.Items[0].SystemMetadata[RuntimeMetadataKeys.WorkflowHasBackgroundActivityWork]);
        Assert.Equal(SourceReferenceId, completedHistory.Items[0].SystemMetadata[RuntimeMetadataKeys.SourceReferenceId]);
        Assert.Equal("background-correlation", completedHistory.Items[0].CorrelationId);
        await harness.CancelAsync(executionId);
        var historyAfterCancel = await WorkflowHistoryAsync(harness, executionId);
        Assert.Single(historyAfterCancel.Items);
        Assert.Equal(completedHistory.Items[0].WorkflowExecutionId, historyAfterCancel.Items[0].WorkflowExecutionId);
        Assert.Equal(completedHistory.Items[0].Status, historyAfterCancel.Items[0].Status);
        Assert.Equal(completedHistory.Items[0].CompletedAt, historyAfterCancel.Items[0].CompletedAt);
        Assert.Equal(completedHistory.Items[0].UpdatedAt, historyAfterCancel.Items[0].UpdatedAt);
        Assert.Equal(completedActivities,
            (await ActivityStatesAsync(harness, executionId))
            .ToDictionary(state => state.Execution.ActivityExecutionId, state => state.Status, StringComparer.Ordinal));
        Assert.Equal(WorkflowExecutionStatus.Completed, await WorkflowStatusAsync(harness, executionId));
        Assert.Equal(1, finalCounter.Count);
        await using (var scope = await CreatePartitionScopeAsync(harness))
        {
            var store = scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>();
            foreach (var item in workers)
                Assert.Equal(BackgroundActivityWorkStatus.Accepted,
                    (await store.FindAsync(executionId, item.Request.WorkId))!.Status);
        }
    }

    [Fact]
    public async Task Cancellation_between_results_retires_held_work_and_rejects_late_delivery()
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var barrier = new BackgroundBodyBarrier();
        var finalCounter = new FinalStepCounter();
        await using var harness = await StartHarnessAsync(executionId, barrier, finalCounter);
        var reference = await PublishAsync(harness);
        await CopyPublicationIntoPartitionAsync(harness, reference);
        await harness.StartPublishedAsync(reference, executionId);

        var workers = await ClaimTwoAsync(harness, executionId);
        var left = workers.Single(item => item.Request.ExecutableNodeId == LeftNodeId);
        var right = workers.Single(item => item.Request.ExecutableNodeId == RightNodeId);
        var worker = harness.Services.GetRequiredService<BackgroundActivityWorker>();
        var workerScope = new PersistenceScope(WorkflowExecutionHarness.Partition.Value);
        var leftRun = worker.ExecuteClaimAsync(workerScope, left.Claim);
        var rightRun = worker.ExecuteClaimAsync(workerScope, right.Claim);
        await Task.WhenAll(
            barrier.WaitUntilEnteredAsync("left", TimeSpan.FromSeconds(10)),
            barrier.WaitUntilEnteredAsync("right", TimeSpan.FromSeconds(10)));

        barrier.Release("left");
        await leftRun.WaitAsync(TimeSpan.FromSeconds(10));
        await DrivePartitionAsync(harness, executionId);
        Assert.Equal(ActivityExecutionStatus.Completed, await ActivityStatusAsync(harness, executionId, LeftNodeId));
        Assert.Equal(ActivityExecutionStatus.Running, await ActivityStatusAsync(harness, executionId, RightNodeId));
        Assert.Equal(0, finalCounter.Count);

        await harness.CancelAsync(executionId);
        barrier.Release("right");
        await rightRun.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WorkflowExecutionStatus.Cancelled, await WorkflowStatusAsync(harness, executionId));
        Assert.Equal(ActivityExecutionStatus.Cancelled, await ActivityStatusAsync(harness, executionId, RightNodeId));
        Assert.Equal(0, finalCounter.Count);

        await using (var scope = await CreatePartitionScopeAsync(harness))
        {
            var store = scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>();
            var cancelled = await store.FindAsync(executionId, right.Request.WorkId);
            Assert.Equal(BackgroundActivityWorkStatus.Cancelled, cancelled!.Status);
            await Assert.ThrowsAsync<BackgroundActivityWorkConflictException>(async () =>
                await store.RecordResultAsync(
                    right.Claim,
                    new BackgroundActivityWorkTerminalResult(
                        BackgroundActivityWorkResultKind.Complete,
                        resultJson: JsonSerializer.Serialize(new BackgroundLeafResult("right-result"))),
                    DateTimeOffset.UtcNow));

            // Simulate a late coordinator wake-up for the cancelled leaf. Its saved invocation pointer must not
            // reopen the leaf or advance the parent after the cancellation checkpoint.
            await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                .EnqueueAsync(right.Request.CoordinatorWorkItem);
        }

        await DrivePartitionAsync(harness, executionId);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, await WorkflowStatusAsync(harness, executionId));
        Assert.Equal(0, finalCounter.Count);
        Assert.Equal(ActivityExecutionStatus.Cancelled, await ActivityStatusAsync(harness, executionId, RightNodeId));
    }

    [Fact]
    public async Task Cancellation_before_claim_keeps_durable_work_unclaimable()
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var barrier = new BackgroundBodyBarrier();
        var finalCounter = new FinalStepCounter();
        await using var harness = await StartHarnessAsync(executionId, barrier, finalCounter);
        var reference = await PublishAsync(harness);
        await CopyPublicationIntoPartitionAsync(harness, reference);
        await harness.StartPublishedAsync(reference, executionId);

        Assert.Equal(ActivityExecutionStatus.Running, await ActivityStatusAsync(harness, executionId, LeftNodeId));
        Assert.Equal(ActivityExecutionStatus.Running, await ActivityStatusAsync(harness, executionId, RightNodeId));
        await harness.CancelAsync(executionId);

        await using var after = await CreatePartitionScopeAsync(harness);
        var afterStore = after.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>();
        Assert.Null(await afterStore.ClaimNextAsync("after-cancel", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        Assert.Equal(WorkflowExecutionStatus.Cancelled, await WorkflowStatusAsync(harness, executionId));
        Assert.Equal(0, barrier.EntryCount);
        Assert.Equal(0, finalCounter.Count);
    }

    [Fact]
    public async Task Cancellation_with_both_background_bodies_held_tombstones_both_claims()
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var barrier = new BackgroundBodyBarrier();
        var finalCounter = new FinalStepCounter();
        await using var harness = await StartHarnessAsync(executionId, barrier, finalCounter);
        var reference = await PublishAsync(harness);
        await CopyPublicationIntoPartitionAsync(harness, reference);
        await harness.StartPublishedAsync(reference, executionId);

        var workers = await ClaimTwoAsync(harness, executionId);
        var worker = harness.Services.GetRequiredService<BackgroundActivityWorker>();
        var workerScope = new PersistenceScope(WorkflowExecutionHarness.Partition.Value);
        var runs = workers.Select(item => worker.ExecuteClaimAsync(workerScope, item.Claim)).ToArray();
        await Task.WhenAll(
            barrier.WaitUntilEnteredAsync("left", TimeSpan.FromSeconds(10)),
            barrier.WaitUntilEnteredAsync("right", TimeSpan.FromSeconds(10)));
        Assert.Equal(2, barrier.EntryCount);
        Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(harness, executionId));

        await harness.CancelAsync(executionId);
        foreach (var item in workers)
            barrier.Release(item.Request.ExecutableNodeId == LeftNodeId ? "left" : "right");
        await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WorkflowExecutionStatus.Cancelled, await WorkflowStatusAsync(harness, executionId));
        Assert.Equal(0, finalCounter.Count);
        foreach (var item in workers)
            Assert.Equal(ActivityExecutionStatus.Cancelled,
                await ActivityStatusAsync(harness, executionId, item.Request.ExecutableNodeId));

        await using (var scope = await CreatePartitionScopeAsync(harness))
        {
            var store = scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>();
            var queue = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            foreach (var item in workers)
            {
                Assert.Equal(BackgroundActivityWorkStatus.Cancelled,
                    (await store.FindAsync(executionId, item.Request.WorkId))!.Status);
                await Assert.ThrowsAsync<BackgroundActivityWorkConflictException>(async () =>
                    await store.RecordResultAsync(
                        item.Claim,
                        new BackgroundActivityWorkTerminalResult(
                            BackgroundActivityWorkResultKind.Complete,
                            resultJson: JsonSerializer.Serialize(new BackgroundLeafResult($"{item.Request.ExecutableNodeId}-late"))),
                        DateTimeOffset.UtcNow));
                await queue.EnqueueAsync(item.Request.CoordinatorWorkItem);
            }
        }

        await DrivePartitionAsync(harness, executionId);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, await WorkflowStatusAsync(harness, executionId));
        Assert.Equal(0, finalCounter.Count);
        Assert.Equal(2, (await ActivityStatesAsync(harness, executionId))
            .Count(state => state.Status == ActivityExecutionStatus.Cancelled &&
                            state.Execution.ExecutableNodeId is LeftNodeId or RightNodeId));
    }

    public void Dispose()
    {
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(file);
    }

    private async Task<WorkflowExecutionHarness> StartHarnessAsync(
        string executionId,
        BackgroundBodyBarrier barrier,
        FinalStepCounter finalCounter,
        PersistenceMode persistenceMode = PersistenceMode.Durable)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => new BackgroundActivitiesFeature
            {
                RunWorkers = false,
                MaxConcurrency = 2,
                LeaseDurationSeconds = 60,
                RenewalIntervalSeconds = 0.1
            }.ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton(barrier);
                services.AddSingleton(finalCounter);
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={_databasePath};Pooling=False",
                    RecoveryContinuationSigningKey = RecoverySigningKey,
                    HierarchyCursorSigningKey = HierarchySigningKey
                })
                .AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
            })
            .WithPersistenceMode(persistenceMode)
            .Build(
                WorkflowExecutionHarness.Identity,
                executionId,
                Enumerable.Range(1, 32).Select(index => $"{executionId}-activity-{index}"));

        foreach (var initializer in harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        harness.InitializeActivityTypes();
        return harness;
    }

    private static Task<WorkflowExecutableSourceReference> PublishAsync(WorkflowExecutionHarness harness) =>
        harness.PublishAsync(NewExecutable(), SourceReferenceId);

    private static async Task CopyPublicationIntoPartitionAsync(
        WorkflowExecutionHarness harness,
        WorkflowExecutableSourceReference reference)
    {
        WorkflowExecutable? executable;
        await using (var source = await harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>()
                         .CreateAsync(new PersistenceScope(PersistenceScope.DefaultValue)))
            executable = await source.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>()
                .FindAsync(reference.ArtifactId);

        await using var target = await CreatePartitionScopeAsync(harness);
        await target.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>()
            .SaveAsync(Assert.IsType<WorkflowExecutable>(executable));
        await target.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
    }

    private static async Task<IReadOnlyList<ClaimedWorker>> ClaimTwoAsync(WorkflowExecutionHarness harness, string executionId)
    {
        await using var scope = await CreatePartitionScopeAsync(harness);
        var store = scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>();
        var result = new List<ClaimedWorker>();
        for (var index = 0; index < 2; index++)
        {
            var claim = await store.ClaimNextAsync($"native-test-worker-{index}", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
            Assert.NotNull(claim);
            var item = await store.FindAsync(executionId, claim.WorkId);
            Assert.NotNull(item);
            result.Add(new ClaimedWorker(claim, item.Request));
        }

        return result;
    }

    private static async Task DrivePartitionAsync(WorkflowExecutionHarness harness, string executionId)
    {
        await using var scope = await CreatePartitionScopeAsync(harness);
        var queue = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var runtime = scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>();
        for (var pass = 0; pass < 4; pass++)
        {
            await runtime.SweepAsync(new RuntimeResumptionSweepRequest(
                backlogBatchSize: 100,
                recoveryScanBatchSize: 100,
                maxExecutionsPerSweep: 100));
            if ((await queue.ListAsync(new RuntimeSchedulerWorkQuery(executionId, limit: 100))).Items.Count == 0)
                return;
        }

        throw new InvalidOperationException($"Scheduler work for '{executionId}' did not drain after four bounded recovery sweeps.");
    }

    private static ValueTask<PersistenceOperationScope> CreatePartitionScopeAsync(WorkflowExecutionHarness harness) =>
        harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value));

    private static async Task<WorkflowExecutionStatus?> WorkflowStatusAsync(WorkflowExecutionHarness harness, string executionId)
    {
        await using var scope = await CreatePartitionScopeAsync(harness);
        return (await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId))?.Status;
    }

    private static async Task<ActivityExecutionStatus> ActivityStatusAsync(
        WorkflowExecutionHarness harness,
        string executionId,
        string nodeId)
    {
        var states = await ActivityStatesAsync(harness, executionId);
        return Assert.Single(states, state => state.Execution.ExecutableNodeId == nodeId).Status;
    }

    private static async Task<IReadOnlyCollection<ActivityExecutionState>> ActivityStatesAsync(
        WorkflowExecutionHarness harness,
        string executionId)
    {
        await using var scope = await CreatePartitionScopeAsync(harness);
        return await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId);
    }

    private static async Task<WorkflowExecutionState?> WorkflowStateAsync(WorkflowExecutionHarness harness, string executionId)
    {
        await using var scope = await CreatePartitionScopeAsync(harness);
        return await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId);
    }

    private static async Task<WorkflowExecutionStatePage> WorkflowHistoryAsync(WorkflowExecutionHarness harness, string executionId)
    {
        await using var scope = await CreatePartitionScopeAsync(harness);
        return await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>()
            .QueryPageAsync(new WorkflowExecutionStatePageQuery(10, WorkflowExecutionId: executionId));
    }

    private static WorkflowExecutable NewExecutable()
    {
        var left = BackgroundNode(LeftNodeId, "left", LeftVariableKey, "left");
        var right = BackgroundNode(RightNodeId, "right", RightVariableKey, "right");
        var fork = InlineNode<ForkOrFinalPassActivity>(ForkNodeId, new Dictionary<string, string>());
        var join = InlineNode<ForkOrFinalPassActivity>(JoinNodeId, new Dictionary<string, string>());
        var flowchartContract = ClrActivityContractTestBuilder.BuildContract(typeof(FlowchartActivity));
        var flowchart = new ExecutableNode(
            executableNodeId: FlowchartNodeId,
            authoredActivityId: $"authored-{FlowchartNodeId}",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: flowchartContract.DescriptorKind,
            descriptorPayload: flowchartContract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [fork, left, right, join])],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(
                    [
                        Connection(ForkNodeId, LeftNodeId),
                        Connection(ForkNodeId, RightNodeId),
                        Connection(LeftNodeId, JoinNodeId),
                        Connection(RightNodeId, JoinNodeId)
                    ],
                    ForkNodeId,
                    new Dictionary<string, FlowchartNodeMetadata> { [ForkNodeId] = new(FlowchartPolicyKinds.ParallelFork) }))),
            activityContract: flowchartContract);
        var final = InlineNode<FinalStepActivity>(FinalNodeId, new Dictionary<string, string>());
        var sequenceContract = ClrActivityContractTestBuilder.BuildContract(typeof(SequenceActivity));
        var sequence = new ExecutableNode(
            executableNodeId: SequenceNodeId,
            authoredActivityId: $"authored-{SequenceNodeId}",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: sequenceContract.DescriptorKind,
            descriptorPayload: sequenceContract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [flowchart, final])],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceExecutableStructure([FlowchartNodeId, FinalNodeId]))),
            activityContract: sequenceContract);

        return WorkflowExecutionHarness.NewExecutable(
            sequence,
            WorkflowExecutionHarness.Identity,
            new RuntimeVariableDeclaration(LeftVariableKey, "left", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline),
            new RuntimeVariableDeclaration(RightVariableKey, "right", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline));
    }

    private static ExecutableNode BackgroundNode(string nodeId, string inputName, string variableKey, string captureName)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(GatedBackgroundActivity));
        var input = contract.Inputs[nameof(GatedBackgroundActivity.Name)];
        var binding = new RuntimeInputBinding(
            input.Key,
            input.Type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(inputName), ValueProtectionPolicy.InstanceInline));
        var projection = Assert.Single(contract.Result.Projections.Values);
        var capture = new RuntimeOutputCapture(
            projection.Key,
            $"{RuntimeWorkflowStateSeed.VariableValueIdPrefix}{captureName}",
            new RuntimeValueTypeDescriptor(projection.Type.Alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Custom,
            captureOnSuccessfulCompletion: true,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = variableKey,
                [RuntimeMetadataKeys.VariableName] = captureName,
                [RuntimeMetadataKeys.StorageDriverKey] = WellKnownRuntimeDurableValueStorageDrivers.Json
            });
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(GatedBackgroundActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: contract.DescriptorKind,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [binding.InputName] = binding },
            metadata: new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { [projection.Key] = capture },
            executionPlacement: ActivityExecutionPlacement.Background);
    }

    private static ExecutableNode InlineNode<TActivity>(
        string nodeId,
        IReadOnlyDictionary<string, string> metadata)
        where TActivity : IActivity
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(TActivity));
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(TActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: contract.DescriptorKind,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: metadata,
            activityContract: contract);
    }

    private static FlowchartConnection Connection(string source, string target) =>
        new(new FlowchartEndpoint(source), new FlowchartEndpoint(target));

    private sealed record ClaimedWorker(BackgroundActivityWorkClaim Claim, BackgroundActivityWorkRequest Request);
}

public sealed class BackgroundBodyBarrier
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _entered = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _released = new(StringComparer.Ordinal);

    public int EntryCount => _entered.Values.Count(signal => signal.Task.IsCompleted);

    public Task WaitUntilEnteredAsync(string name, TimeSpan timeout) =>
        _entered.GetOrAdd(name, _ => NewSignal()).Task.WaitAsync(timeout);

    public void Release(string name) => _released.GetOrAdd(name, _ => NewSignal()).TrySetResult();

    public async Task ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        _entered.GetOrAdd(name, _ => NewSignal()).TrySetResult();
        await _released.GetOrAdd(name, _ => NewSignal()).Task.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class FinalStepCounter
{
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public void Increment() => Interlocked.Increment(ref _count);
}

[BackgroundExecutionSafe]
public sealed class GatedBackgroundActivity(BackgroundBodyBarrier barrier) : Activity<BackgroundLeafResult>
{
    [ActivityInput]
    public string Name { get; set; } = string.Empty;

    protected override async ValueTask<ActivityTransition<BackgroundLeafResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        await barrier.ExecuteAsync(Name, context.CancellationToken);
        return ActivityTransition.Complete(new BackgroundLeafResult($"{Name}-result"));
    }
}

public sealed class BackgroundLeafResult(string value)
{
    [Output(Key = "value", Path = "value")]
    public string Value { get; } = value;
}

public sealed class ForkOrFinalPassActivity : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

public sealed class FinalStepActivity(FinalStepCounter counter) : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        counter.Increment();
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
