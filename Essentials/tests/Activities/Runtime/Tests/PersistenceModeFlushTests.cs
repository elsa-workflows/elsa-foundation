using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// What each persistence mode commits, observed on the durable store the harness wraps: which checkpoints reach it and
/// how often. The runs use real activities through the production runtime over in-memory stores.
/// </summary>
public sealed class PersistenceModeFlushTests
{
    private const int StraightLineLength = 7;
    private const string CountingNodeId = "node-counting";
    private const string WaitingNodeId = "node-waiting";
    private static readonly string[] ActivityExecutionIds = Enumerable.Range(0, 32).Select(index => $"actexec-{index}").ToArray();

    public PersistenceModeFlushTests() => CountingReplaySafeActivity.Reset();

    [Fact]
    public async Task Balanced_counts_MaxActivitiesPerCommit_in_activities_not_checkpoints()
    {
        var uncapped = await RunStraightLineAsync(PersistenceMode.Balanced, maxActivitiesPerCommit: 50);
        Assert.Equal(1, uncapped.Commits);

        // Each activity produces several checkpoints, so a cap counted in checkpoints would commit many times here. A cap
        // equal to the number of activities the run touches fits the whole run in one commit; one fewer forces exactly one
        // intermediate commit when the last new activity arrives.
        var atActivityCount = await RunStraightLineAsync(PersistenceMode.Balanced, maxActivitiesPerCommit: uncapped.Activities);
        var belowActivityCount = await RunStraightLineAsync(PersistenceMode.Balanced, maxActivitiesPerCommit: uncapped.Activities - 1);

        Assert.Equal(1, atActivityCount.Commits);
        Assert.Equal(2, belowActivityCount.Commits);
        Assert.Equal(uncapped.Snapshot, belowActivityCount.Snapshot);
    }

    [Fact]
    public async Task Balanced_with_the_smallest_cap_still_completes()
    {
        // With a cap of one, a parent's checkpoint can bring two activities into an empty segment at once. That segment
        // must take the checkpoint rather than fold an empty buffer, or the drain loses the in-flight work and the run
        // stops in Running with nothing queued.
        var uncapped = await RunStraightLineAsync(PersistenceMode.Balanced, maxActivitiesPerCommit: 50);
        var smallest = await RunStraightLineAsync(PersistenceMode.Balanced, maxActivitiesPerCommit: 1);

        Assert.Equal(uncapped.Snapshot, smallest.Snapshot);
        Assert.True(smallest.Commits > uncapped.Commits);
    }

    [Fact]
    public async Task Ephemeral_has_no_intermediate_commits()
    {
        // The cap that splits a Balanced run applies to Balanced only: the same run under Ephemeral commits once, at
        // completion.
        var balanced = await RunStraightLineAsync(PersistenceMode.Balanced, maxActivitiesPerCommit: 1);
        var ephemeral = await RunStraightLineAsync(PersistenceMode.Ephemeral, maxActivitiesPerCommit: 1);

        Assert.True(balanced.Commits > 1);
        Assert.Equal(1, ephemeral.Commits);
        Assert.Equal(balanced.Snapshot, ephemeral.Snapshot);
    }

    [Theory]
    [InlineData(PersistenceMode.Ephemeral, false, false)]
    [InlineData(PersistenceMode.Ephemeral, true, true)]
    [InlineData(PersistenceMode.Balanced, false, true)]
    [InlineData(PersistenceMode.Durable, false, true)]
    public async Task Only_Ephemeral_without_completion_records_drops_the_record_of_a_run_that_was_never_committed(
        PersistenceMode mode,
        bool recordCompletion,
        bool expectRecord)
    {
        await using var harness = NewHarness(mode, recordCompletion);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSequence(
            "node-sequence",
            NewCountingNode(),
            WorkflowExecutionHarness.NewReplaySafeProbeNode("node-probe"))));

        if (expectRecord)
        {
            Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
            Assert.Equal(3, run.ActivityStates.Count);
        }
        else
        {
            // No trace: nothing reached the durable store, and nothing is left that could run it again.
            Assert.Null(run.WorkflowState);
            Assert.Empty(run.ActivityStates);
            Assert.Empty(harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
            var durableValues = await harness.Services.GetRequiredService<IDurableValueStateStore>()
                .ListPageAsync(new DurableValueStatePageQuery(harness.ExecutionId));
            Assert.Empty(durableValues.Items);
        }

        // Either way the queued start is consumed, so nothing is left that would run the workflow again, and the
        // activity ran exactly once.
        Assert.Empty(await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId)));
        Assert.Equal(1, CountingReplaySafeActivity.Executions);
    }

    [Fact]
    public async Task Ephemeral_without_completion_records_still_records_a_run_committed_earlier_in_the_same_pass()
    {
        // An activity that is not safe to repeat is committed before it runs, which gives the run a record. From then on
        // its completion must be committed too, or the stored run would stay Running forever.
        await using var harness = NewHarness(PersistenceMode.Ephemeral, recordCompletion: false);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSequence(
            "node-sequence",
            WorkflowExecutionHarness.NewProbeNode("node-not-replay-safe"),
            NewCountingNode())));

        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        Assert.All(run.ActivityStates, state => Assert.Equal(ActivityExecutionStatus.Completed, state.Status));
        Assert.Equal(1, CountingReplaySafeActivity.Executions);
    }

    [Fact]
    public async Task Ephemeral_without_completion_records_still_records_the_completion_of_a_run_that_suspended_before()
    {
        // The suspension committed the run, so a later drain that completes it starts out knowing the run has a record.
        await using var harness = NewHarness(PersistenceMode.Ephemeral, recordCompletion: false);
        var executable = NewWaitingExecutable();

        var suspended = await harness.RunAsync(executable);
        Assert.Equal(WorkflowExecutionStatus.Running, suspended.WorkflowState?.Status);
        var waiting = suspended.State(WaitingNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, waiting.Status);

        var resumed = await harness.ResumeAsync(
            executable.Identity,
            bookmarkId: Assert.Single(waiting.BookmarkIds),
            activityExecutionId: waiting.InvocationId,
            executableNodeId: WaitingNodeId,
            resumeTargetId: WaitingResumeTargetId,
            stimulusType: "TestWaitReplaySafe",
            stimulusHash: "wait:99",
            input: JsonSerializer.SerializeToElement(new WaitTrigger(Released: true)));

        Assert.Equal(WorkflowExecutionStatus.Completed, resumed.WorkflowState?.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, resumed.State(WaitingNodeId).Status);
        Assert.Equal(1, CountingReplaySafeActivity.Executions);
    }

    private static WorkflowExecutionHarness NewHarness(PersistenceMode mode, bool recordCompletion, int? maxActivitiesPerCommit = null) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .WithPersistenceMode(mode, maxActivitiesPerCommit, recordCompletion)
            .Build(ActivityExecutionIds);

    private static async Task<(int Activities, int Commits, IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)> Snapshot)> RunStraightLineAsync(
        PersistenceMode mode,
        int maxActivitiesPerCommit)
    {
        await using var harness = NewHarness(mode, recordCompletion: true, maxActivitiesPerCommit);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSequence(
            "node-sequence",
            Enumerable.Range(0, StraightLineLength)
                .Select(index => WorkflowExecutionHarness.NewReplaySafeProbeNode($"node-probe-{index}"))
                .ToArray())));

        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        var commits = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits().Count;
        var snapshot = run.ActivityStates
            .Select(state => (state.Execution.ExecutableNodeId, state.Status))
            .OrderBy(entry => entry.ExecutableNodeId, StringComparer.Ordinal)
            .ToArray();
        return (run.ActivityStates.Count, commits, snapshot);
    }

    private static readonly string WaitingResumeTargetId = $"resume:{WaitingNodeId}:{ReplaySafeWaitingActivity.ResumeTargetKey}";

    // A ReplaySafe wait followed by a ReplaySafe counter: the only commit before completion is the suspension.
    private static WorkflowExecutable NewWaitingExecutable() =>
        new(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: NewSequence("node-sequence", NewClrNode(WaitingNodeId, typeof(ReplaySafeWaitingActivity)), NewCountingNode()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [WaitingResumeTargetId] = new(
                    WaitingResumeTargetId,
                    WaitingNodeId,
                    "ResumeAsync",
                    new Dictionary<string, string>(),
                    LocalResumeTargetId: ReplaySafeWaitingActivity.ResumeTargetKey)
            },
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static ExecutableNode NewCountingNode() => NewClrNode(CountingNodeId, typeof(CountingReplaySafeActivity));

    private static ExecutableNode NewClrNode(string nodeId, Type activityType) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

    private static ExecutableNode NewSequence(string nodeId, params ExecutableNode[] activities) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, activities)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = activities.Select(activity => activity.ExecutableNodeId).ToArray() })));
}

/// <summary>
/// A ReplaySafe leaf that counts how often it runs, so a test can tell a run that executed once from one that was
/// started again. The count is process-wide; the one test class that uses it resets it per test and its tests run one
/// at a time.
/// </summary>
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class CountingReplaySafeActivity : Activity<ActivityUnit>
{
    private static int _executions;

    public static int Executions => Volatile.Read(ref _executions);

    public static void Reset() => Interlocked.Exchange(ref _executions, 0);

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        Interlocked.Increment(ref _executions);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}
