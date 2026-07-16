using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Parallel;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.Parallel.Tests;

/// <summary>
/// In-process execution coverage for the <c>Parallel</c> fork/join composite running through the real
/// workflow agent on the shared <see cref="WorkflowExecutionHarness"/>. Asserts every branch runs, that
/// each branch forks under a distinct engine <c>BranchId</c>, that the join genuinely waits for all (or a
/// configured threshold of) branches before the composite completes exactly once, that branch outputs do
/// not collide, and that a branch running <c>Finish</c> ends the run terminally and cancels the siblings.
/// </summary>
public sealed class ParallelRuntimeTests
{
    private const string ParallelNodeId = "node-parallel";
    private static readonly (string Name, string Node)[] ThreeBranches = [("a", "node-a"), ("b", "node-b"), ("c", "node-c")];

    [Fact]
    public async Task AllBranchesRun_AndParallelCompletesOnce_AfterEveryBranchFinishes()
    {
        await using var harness = NewHarness("actexec-parallel", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutable(ThreeBranches));

        // All three probe branches ran.
        run.AssertRan("node-a", "node-b", "node-c");

        // The Parallel completed exactly once with Done — the join waited for all branches.
        var parallelStates = run.States(ParallelNodeId);
        var completed = Assert.Single(parallelStates, state => state.Status == ActivityExecutionStatus.Completed);
        Assert.Equal([ActivityOutcomes.Done], WorkflowExecutionRun.CompletionOutcomes(completed));
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task EachBranchForksUnderDistinctBranchId()
    {
        await using var harness = NewHarness("actexec-parallel", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutable(ThreeBranches));

        var branchIds = ThreeBranches
            .Select(branch => run.State(branch.Node).BranchId)
            .ToArray();

        // Every branch carries a BranchId, and all are distinct (no cross-branch collision).
        Assert.All(branchIds, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Equal(branchIds.Length, branchIds.Distinct().Count());
    }

    [Fact]
    public async Task JoinWaitsForAllBranches_ParallelNotCompletedUntilLastBranch()
    {
        // With three branches the Parallel must remain incomplete until the third branch finishes. We prove
        // the join "waits" structurally: the completed Parallel state exists only once, and every branch
        // produced a completed execution state before it (all three ran). A premature join would have left
        // at least one branch unrun.
        await using var harness = NewHarness("actexec-parallel", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutable(ThreeBranches));

        foreach (var (_, node) in ThreeBranches)
            Assert.Equal(ActivityExecutionStatus.Completed, run.State(node).Status);

        Assert.Single(run.States(ParallelNodeId), state => state.Status == ActivityExecutionStatus.Completed);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task SingleBranch_RunsOnce_AndCompletes()
    {
        await using var harness = NewHarness("actexec-parallel", "actexec-a");

        var run = await harness.RunAsync(NewExecutable([("a", "node-a")]));

        run.AssertRan("node-a");
        run.AssertOutcomes(ParallelNodeId, ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Threshold_CompletesAfterSubset_WithoutRequiringEveryBranch()
    {
        // Threshold = 2 of 3: the join completes once two branches finish. All branches are still forked
        // (single-threaded scheduler drains them), but the composite must complete exactly once.
        await using var harness = NewHarness("actexec-parallel", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutable(ThreeBranches, threshold: 2));

        Assert.Single(run.States(ParallelNodeId), state => state.Status == ActivityExecutionStatus.Completed);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task NoBranchOutputCollision_AcrossBranches()
    {
        // Each branch is a distinct executable node under a distinct BranchId, so each produces its own
        // execution state; none overwrites another. Assert every branch has its own completed state.
        await using var harness = NewHarness("actexec-parallel", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutable(ThreeBranches));

        var branchExecutionIds = ThreeBranches
            .Select(branch => run.State(branch.Node).Execution.ActivityExecutionId)
            .ToArray();
        Assert.Equal(branchExecutionIds.Length, branchExecutionIds.Distinct().Count());
    }

    [Fact]
    public async Task FinishInsideBranch_EndsWorkflowTerminally_WithoutCompletingTheJoin()
    {
        // One branch runs Finish; the others are probes. Finish ends the whole run with a successful
        // outcome: the workflow reaches Completed terminally and (per #293) the drainer abandons any
        // still-queued sibling branch work rather than dispatching post-completion state. The Parallel's
        // own join never fires — the composite stays Running — because Finish short-circuits the run.
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<ParallelConstructor>()
            .WithConstructor<FinishConstructor>()
            .WithProbeLeaf()
            .Build("actexec-parallel", "actexec-finish", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutableWithFinishBranch(), allowPendingWorkOnTerminalCompletion: true);

        run.AssertRan("node-finish");
        run.AssertWorkflowCompleted();
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        Assert.NotNull(run.WorkflowState?.CompletedAt);

        // The run ended via Finish, not via the Parallel join: the composite never reached Completed.
        Assert.DoesNotContain(run.States(ParallelNodeId), state => state.Status == ActivityExecutionStatus.Completed);
    }

    [Fact]
    public async Task FaultedBranch_FaultsTheComposite_InsteadOfHanging()
    {
        // Default (all-branches) threshold with one faulting branch: the join can never be satisfied, so the
        // composite must fault deterministically (surfacing an incident) rather than hang Running forever (#308).
        await using var harness = NewFaultAwareHarness("actexec-parallel", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutableWithFaultingBranch(ThreeBranches));

        // The faulting branch faulted and recorded its own incident; the probe branches still ran.
        var faultedBranch = run.State("node-a");
        Assert.Equal(ActivityExecutionStatus.Faulted, faultedBranch.Status);
        Assert.NotEmpty(faultedBranch.IncidentIds);
        run.AssertRan("node-b", "node-c");

        // The composite faulted (with a composite incident) and never completed — no longer stuck Running.
        var parallel = run.State(ParallelNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, parallel.Status);
        Assert.NotEmpty(parallel.IncidentIds);
        Assert.DoesNotContain(run.States(ParallelNodeId), state => state.Status == ActivityExecutionStatus.Completed);
    }

    [Fact]
    public async Task Threshold_MetBySuccessfulBranches_CompletesDespiteFaultedBranch()
    {
        // Threshold = 2 of 3 with one faulting branch: the two probe branches satisfy the join, so the composite
        // completes Done even though one branch faulted (#308). The faulted branch keeps its own incident.
        await using var harness = NewFaultAwareHarness("actexec-parallel", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutableWithFaultingBranch(ThreeBranches, threshold: 2));

        Assert.Single(run.States(ParallelNodeId), state => state.Status == ActivityExecutionStatus.Completed);
        Assert.DoesNotContain(run.States(ParallelNodeId), state => state.Status == ActivityExecutionStatus.Faulted);
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-a").Status);
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<ParallelConstructor>()
            .WithProbeLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutionHarness NewFaultAwareHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<ParallelConstructor>()
            .WithProbeLeaf()
            .WithFaultingLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutableWithFaultingBranch((string Name, string Node)[] branches, int? threshold = null)
    {
        // The first branch faults during execution; the remaining branches are probes.
        var childSlots = branches
            .Select((branch, index) => new ExecutableChildSlot(
                ParallelActivity.BranchSlotName(branch.Name),
                [index == 0 ? WorkflowExecutionHarness.NewFaultingNode(branch.Node) : WorkflowExecutionHarness.NewProbeNode(branch.Node)]))
            .ToList();

        return WorkflowExecutionHarness.NewExecutable(NewParallelRoot(childSlots, branches, threshold));
    }

    private static WorkflowExecutable NewExecutable((string Name, string Node)[] branches, int? threshold = null)
    {
        var childSlots = branches
            .Select(branch => new ExecutableChildSlot(ParallelActivity.BranchSlotName(branch.Name), [WorkflowExecutionHarness.NewProbeNode(branch.Node)]))
            .ToList();

        return WorkflowExecutionHarness.NewExecutable(NewParallelRoot(childSlots, branches, threshold));
    }

    private static WorkflowExecutable NewExecutableWithFinishBranch()
    {
        var finishNode = new ExecutableNode(
            executableNodeId: "node-finish",
            authoredActivityId: "authored-finish",
            activityType: typeof(Finish).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(FinishDescriptor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new FinishDescriptor())),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        var branches = new (string Name, string Node)[] { ("a", "node-finish"), ("b", "node-b"), ("c", "node-c") };
        var childSlots = new List<ExecutableChildSlot>
        {
            new(ParallelActivity.BranchSlotName("a"), [finishNode]),
            new(ParallelActivity.BranchSlotName("b"), [WorkflowExecutionHarness.NewProbeNode("node-b")]),
            new(ParallelActivity.BranchSlotName("c"), [WorkflowExecutionHarness.NewProbeNode("node-c")])
        };

        return WorkflowExecutionHarness.NewExecutable(NewParallelRoot(childSlots, branches, threshold: null));
    }

    private static ExecutableNode NewParallelRoot(
        IReadOnlyCollection<ExecutableChildSlot> childSlots,
        (string Name, string Node)[] branches,
        int? threshold) =>
        new(
            executableNodeId: ParallelNodeId,
            authoredActivityId: "authored-parallel",
            activityType: typeof(ParallelActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(ParallelConstructor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new ParallelDescriptor())),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                ParallelActivity.StructureKind,
                ParallelActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    branches = branches.Select(branch => new { name = branch.Name, activity = branch.Node }).ToArray(),
                    threshold
                })));

    private sealed record ParallelDescriptor;

    private sealed class ParallelConstructor : IActivityConstructor<ParallelDescriptor>
    {
        public static string ConsumerKeyValue => typeof(ParallelDescriptor).FullName!;
        public string ConsumerKey => ConsumerKeyValue;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new ParallelActivity());

        public ValueTask<IActivity> Construct(ParallelDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new ParallelActivity());
    }

    private sealed record FinishDescriptor
    {
        public static string ConsumerKeyValue => typeof(FinishDescriptor).FullName!;
    }

    private sealed class FinishConstructor : IActivityConstructor<FinishDescriptor>
    {
        public string ConsumerKey => FinishDescriptor.ConsumerKeyValue;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new Finish());

        public ValueTask<IActivity> Construct(FinishDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new Finish());
    }
}
