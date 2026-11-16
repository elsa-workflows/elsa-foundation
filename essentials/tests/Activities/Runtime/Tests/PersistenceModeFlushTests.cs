using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Runtime.Tests.PersistenceModeTestRuns;
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

    private readonly ActivityRunLog _runs = new();

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
        await AssertNothingQueuedAsync(harness);
        Assert.Equal(1, _runs.Executions);
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
        Assert.Equal(1, _runs.Executions);
    }

    // ---- What every mode commits -------------------------------------------------------------------------------------
    // Each theory runs the same workflow under every mode, including Ephemeral with completion records off, whose runs
    // are never committed unless something forces it. Every activity in these workflows is ReplaySafe, so nothing but
    // the boundary under test commits the run.

    public static TheoryData<PersistenceMode, bool> EveryMode() => new()
    {
        { PersistenceMode.Durable, true },
        { PersistenceMode.Balanced, true },
        { PersistenceMode.Ephemeral, true },
        { PersistenceMode.Ephemeral, false }
    };

    [Theory]
    [MemberData(nameof(EveryMode))]
    public async Task A_suspension_and_its_resume_are_committed_in_every_mode(PersistenceMode mode, bool recordCompletion)
    {
        await using var harness = NewHarness(mode, recordCompletion);
        var executable = NewWaitingExecutable();

        var suspended = await harness.RunAsync(executable);

        // The bookmark and the waiting run are durable before the drain returns.
        Assert.Equal(WorkflowExecutionStatus.Running, suspended.WorkflowState?.Status);
        var waiting = suspended.State(WaitingNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, waiting.Status);
        var bookmarkId = Assert.Single(waiting.BookmarkIds);
        var bookmarks = harness.Services.GetRequiredService<IBookmarkStateStore>();
        Assert.NotNull(await bookmarks.FindAsync(harness.ExecutionId, bookmarkId));
        Assert.Contains(
            harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits(),
            commit => commit.Commit.StateChanges.Bookmarks.Any(change => change.StateId == bookmarkId));

        // The resume that completes the run is committed too: the suspension gave the run a record, so even Ephemeral
        // without completion records keeps its completion, rather than leaving a stored run that looks unfinished.
        var resumed = await harness.ResumeAsync(
            executable.Identity,
            bookmarkId: bookmarkId,
            activityExecutionId: waiting.InvocationId,
            executableNodeId: WaitingNodeId,
            resumeTargetId: WaitingResumeTargetId,
            stimulusType: "TestWaitReplaySafe",
            stimulusHash: "wait:99",
            input: JsonSerializer.SerializeToElement(new WaitTrigger(Released: true)));

        Assert.Equal(WorkflowExecutionStatus.Completed, resumed.WorkflowState?.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, resumed.State(WaitingNodeId).Status);
        Assert.Null(await bookmarks.FindAsync(harness.ExecutionId, bookmarkId));
        Assert.Equal(1, _runs.Executions);
    }

    [Theory]
    [MemberData(nameof(EveryMode))]
    public async Task A_fault_is_committed_in_every_mode(PersistenceMode mode, bool recordCompletion)
    {
        await using var harness = NewHarness(mode, recordCompletion);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSequence(
            "node-sequence",
            NewCountingNode(),
            NewClrNode("node-fault", typeof(Fault)))));

        // A run that was never committed before still records its fault: the exception covers completion only.
        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-fault").Status);
        Assert.Equal(ActivityExecutionStatus.Completed, run.State(CountingNodeId).Status);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(harness.ExecutionId));
        Assert.Equal("node-fault", incident.ExecutableNodeId);
    }

    [Theory]
    [MemberData(nameof(EveryMode))]
    public async Task A_cancellation_is_committed_in_every_mode(PersistenceMode mode, bool recordCompletion)
    {
        await using var harness = NewHarness(mode, recordCompletion);
        await harness.RunAsync(NewWaitingExecutable());

        await DeliverAsync(harness, WorkflowExecutionCommandKind.Cancel, WorkflowExecutionActorActivationReason.ControlPlaneCommand);

        var run = await harness.ReadRunAsync(harness.ExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, run.WorkflowState?.Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State(WaitingNodeId).Status);
        await AssertNothingQueuedAsync(harness);
    }

    [Theory]
    [MemberData(nameof(EveryMode))]
    public async Task A_handler_fault_records_its_poison_and_incident_and_keeps_the_progress_before_it_in_every_mode(
        PersistenceMode mode,
        bool recordCompletion)
    {
        // A handler that throws is not an activity fault: the drain records the work item as poison and stops, and an
        // observer then raises a blocking incident. Under the folding modes the activity before it is still only in
        // memory at that point, so the end of the pass must commit it, and the poisoned item must not stay queued to be
        // delivered again.
        await using var harness = NewHarness(mode, recordCompletion, services => InvokeHandlerInterruptions.FailInvocationOf(services, PoisonedNodeId));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(NewSequence(
                "node-sequence",
                NewCountingNode(),
                WorkflowExecutionHarness.NewReplaySafeProbeNode(PoisonedNodeId),
                WorkflowExecutionHarness.NewReplaySafeProbeNode("node-never-reached"))),
            allowPendingWorkOnTerminalCompletion: false,
            allowAcceptedButFaulted: true);

        Assert.NotNull(run.WorkflowState);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState!.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, run.State(CountingNodeId).Status);
        Assert.False(run.DidRun("node-never-reached"));
        Assert.Single(await harness.Services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(harness.ExecutionId));
        Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(harness.ExecutionId));
        await AssertNothingQueuedAsync(harness);
        Assert.Equal(1, _runs.Executions);
    }

    [Theory]
    [InlineData(PersistenceMode.Balanced, true)]
    [InlineData(PersistenceMode.Ephemeral, true)]
    [InlineData(PersistenceMode.Ephemeral, false)]
    public async Task A_node_that_loses_ownership_mid_pass_commits_nothing_and_a_peer_completes_the_run_once(
        PersistenceMode mode,
        bool recordCompletion)
    {
        // Another node takes the run's ownership halfway through this node's pass, while the pass is still only in
        // memory. Every commit this node makes after that is refused, so nothing of the pass reaches the store and the
        // queued work stays for the new owner, which runs those activities again and completes the run once. That holds
        // for a completion Ephemeral would drop too: dropping it touches the durable queue, so it checks ownership first.
        // Durable commits the first activity before the steal, so it has a test of its own below.
        await using var harness = NewHarness(mode, recordCompletion);
        var executable = WorkflowExecutionHarness.NewExecutable(NewSequence(
            "node-sequence",
            NewCountingNode(),
            NewClrNode("node-steal", typeof(LeaseStealingReplaySafeActivity)),
            WorkflowExecutionHarness.NewReplaySafeProbeNode("node-after")));

        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunAsync(executable));

        var commits = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        Assert.Empty(commits.ListCommits());
        Assert.NotEmpty(await QueuedWorkAsync(harness));

        await DeliverAsync(harness, WorkflowExecutionCommandKind.RunSchedulerWork, WorkflowExecutionActorActivationReason.Recovery);

        var completions = commits.ListCommits().Count(commit => commit.Commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowCompleted);
        var run = await harness.ReadRunAsync(harness.ExecutionId);
        if (recordCompletion)
        {
            Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
            Assert.Equal(1, completions);
        }
        else
        {
            // Nothing of the run was ever committed, so its completion leaves no record.
            Assert.Null(run.WorkflowState);
            Assert.Equal(0, completions);
        }

        await AssertNothingQueuedAsync(harness);
        Assert.Equal(2, _runs.Executions);
    }

    [Fact]
    public async Task Under_Durable_a_node_that_loses_ownership_keeps_what_it_committed_and_a_peer_completes_the_run_once()
    {
        // Durable commits the first activity when the next one starts, so that commit lands before another node takes
        // the run, together with the continuation that schedules the next activity. The commit that would record the
        // stealing activity is refused. That refusal is not a fault of the work this node was running: nothing is
        // recorded as poison, and the committed continuation is still pending, so the new owner delivers it, runs the
        // rest without running the committed activity again, and completes the run once.
        await using var harness = NewHarness(PersistenceMode.Durable, recordCompletion: true);
        var executable = WorkflowExecutionHarness.NewExecutable(NewSequence(
            "node-sequence",
            NewCountingNode(),
            NewClrNode("node-steal", typeof(LeaseStealingReplaySafeActivity)),
            WorkflowExecutionHarness.NewReplaySafeProbeNode("node-after")));

        await Assert.ThrowsAnyAsync<Exception>(() => harness.RunAsync(executable));

        var commits = harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        Assert.Contains(commits.ListCommits(), commit => RecordsCompleted(commit.Commit, CountingNodeId));
        Assert.DoesNotContain(commits.ListCommits(), commit => RecordsCompleted(commit.Commit, "node-steal"));
        Assert.Empty(await harness.Services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(harness.ExecutionId));
        Assert.Contains(
            await commits.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(DateTimeOffset.MaxValue, limit: 10, harness.ExecutionId)),
            item => item.Intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork);

        // The new owner recovers the run the way the resumption sweep does: it delivers the run's pending continuations,
        // then drains the run's queued work.
        await using (var scope = harness.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));
        await DeliverAsync(harness, WorkflowExecutionCommandKind.RunSchedulerWork, WorkflowExecutionActorActivationReason.Recovery);

        var run = await harness.ReadRunAsync(harness.ExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        Assert.Equal(1, commits.ListCommits().Count(commit => commit.Commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowCompleted));
        Assert.Empty(await harness.Services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(harness.ExecutionId));
        await AssertNothingQueuedAsync(harness);
        Assert.Equal(1, _runs.Executions);
    }

    private static bool RecordsCompleted(RuntimeCheckpointCommit commit, string executableNodeId) =>
        commit.StateChanges.ActivityExecutions.Any(change =>
            change.Operation == RuntimeStateChangeOperation.Upsert &&
            change.State.Status == ActivityExecutionStatus.Completed &&
            StringComparer.Ordinal.Equals(change.State.Execution.ExecutableNodeId, executableNodeId));

    private const string PoisonedNodeId = "node-poisoned";

    // Nothing is left queued that would run the workflow again.
    private static async Task AssertNothingQueuedAsync(WorkflowExecutionHarness harness) =>
        Assert.Empty(await QueuedWorkAsync(harness));

    private WorkflowExecutionHarness NewHarness(
        PersistenceMode mode,
        bool recordCompletion,
        Action<IServiceCollection>? configure = null,
        int? maxActivitiesPerCommit = null) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => services.AddSingleton(_runs))
            .WithFeature(services => configure?.Invoke(services))
            .WithPersistenceMode(mode, maxActivitiesPerCommit, recordCompletion)
            .Build(ActivityExecutionIds);

    private async Task<(int Activities, int Commits, IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)> Snapshot)> RunStraightLineAsync(
        PersistenceMode mode,
        int maxActivitiesPerCommit)
    {
        await using var harness = NewHarness(mode, recordCompletion: true, maxActivitiesPerCommit: maxActivitiesPerCommit);

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
