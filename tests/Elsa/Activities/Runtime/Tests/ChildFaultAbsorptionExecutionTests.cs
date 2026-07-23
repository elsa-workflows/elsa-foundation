using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 115: a structural parent absorbs a child fault during the child-fault evaluation — the
/// incident resolves and the faulted child's leftover subtree is reclaimed in the same checkpoint
/// commit as the parent's continuation.
/// </summary>
public sealed class ChildFaultAbsorptionExecutionTests
{
    [Fact]
    public async Task LeafChildFault_AbsorbedWithDefer_ResolvesIncidentAndWorkflowCompletes()
    {
        await using var harness = NewHarness(
            new FaultAbsorptionDirective(),
            "actexec-parent", "actexec-f", "actexec-a", "actexec-a-child");

        var run = await harness.RunAsync(StructuralExecutionTestSupport.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(FaultAbsorbingParentActivity),
                WorkflowExecutionHarness.NewFaultingNode("node-f"),
                StructuralExecutionTestSupport.NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child")))));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-f").Status);
        var incident = Assert.Single(await ListIncidentsAsync(harness));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(IncidentResolutionAction.Continue, incident.ResolutionAction);
        Assert.NotNull(incident.ResolvedAt);
        Assert.Equal("actexec-parent", incident.Metadata[RuntimeMetadataKeys.FaultAbsorbedBy]);
        Assert.Equal("test-absorb", incident.Metadata[RuntimeMetadataKeys.FaultAbsorptionReason]);
    }

    [Fact]
    public async Task CompositeChildFault_AbsorbedWithComplete_ReclaimsDescendantsAndResolvesIncident()
    {
        await using var harness = NewHarness(
            new FaultAbsorptionDirective { CompleteOnAbsorb = true },
            "actexec-parent", "actexec-b", "actexec-a", "actexec-b-wait", "actexec-b-fault", "actexec-a-child");

        var run = await harness.RunAsync(StructuralExecutionTestSupport.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(FaultAbsorbingParentActivity),
                StructuralExecutionTestSupport.NewStructuralNode("node-b", typeof(RefaultingForkActivity),
                    StructuralExecutionTestSupport.NewWaitingNode("node-b-wait"),
                    WorkflowExecutionHarness.NewFaultingNode("node-b-fault")),
                StructuralExecutionTestSupport.NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child"))),
            waitingNodeIds: ["node-b-wait"]));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-b").Status);
        var reclaimed = run.State("node-b-wait");
        Assert.Equal(ActivityExecutionStatus.Cancelled, reclaimed.Status);
        Assert.Equal("FaultAbsorbed", reclaimed.SubStatus);
        Assert.Empty(await harness.Services.GetRequiredService<IBookmarkStateStore>()
            .ListAllBookmarkStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId));

        var incidents = await ListIncidentsAsync(harness);
        Assert.Equal(2, incidents.Count);
        var absorbed = Assert.Single(incidents, incident => incident.ExecutableNodeId == "node-b");
        Assert.Equal(IncidentStatus.Resolved, absorbed.Status);
        var suppressed = Assert.Single(incidents, incident => incident.ExecutableNodeId == "node-b-fault");
        Assert.Equal(IncidentStatus.Suppressed, suppressed.Status);
    }

    [Fact]
    public async Task WrongIncidentId_FaultsTheEvaluation()
    {
        var run = await RunLeafFaultScenarioAsync(new FaultAbsorptionDirective { OverrideIncidentId = "incident-wrong" });

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("but this evaluation carries incident", parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateAbsorption_FaultsTheEvaluation()
    {
        var run = await RunLeafFaultScenarioAsync(new FaultAbsorptionDirective { StageTwice = true });

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("more than once", parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FaultContinuationWithAbsorption_FaultsTheEvaluation()
    {
        var run = await RunLeafFaultScenarioAsync(new FaultAbsorptionDirective { FaultInsteadOfContinue = true });

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("cannot also absorb", parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbsorptionInCompletionEvaluation_FaultsTheEvaluation()
    {
        await using var harness = NewHarness(
            new FaultAbsorptionDirective { AbsorbInCompletionEvaluation = true },
            "actexec-parent", "actexec-a", "actexec-a-child");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(FaultAbsorbingParentActivity),
                    StructuralExecutionTestSupport.NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child")))),
            allowPendingWorkOnTerminalCompletion: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("only valid in a child-fault evaluation", parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitialStructuralExecution_CannotStageAbsorption()
    {
        await using var harness = NewHarness(
            new FaultAbsorptionDirective(),
            "actexec-parent", "actexec-child");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(InitialAbsorptionStructuralActivity),
                    WorkflowExecutionHarness.NewProbeNode("node-child"))),
            allowPendingWorkOnTerminalCompletion: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("cannot absorb child faults", parent.Fault!.Message, StringComparison.Ordinal);
    }

    // #989: a parent that absorbs a child fault AND schedules a NEW child during the SAME fault evaluation
    // (Defer branch) must receive that new child's clean completion as an ordinary completion, not as a
    // fabricated fault of the original (already-resolved) incident. Before the fix, the fault-evaluation work
    // item's fault-scoped CommandMetadata leaked onto the derived child schedule and misclassified its clean
    // completion as a fault.
    [Fact]
    public async Task ChildFault_AbsorbedWithDeferThatSchedulesNewChild_DeliversCleanCompletion()
    {
        await using var harness = ReschedulingHarness(new FaultAbsorptionDirective());

        var run = await harness.RunAsync(NewReschedulingAbsorptionExecutable());

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        run.AssertCompleted("node-g");
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-f").Status);
        var incident = Assert.Single(await ListIncidentsAsync(harness));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(IncidentResolutionAction.Continue, incident.ResolutionAction);
        Assert.Equal("actexec-parent", incident.Metadata[RuntimeMetadataKeys.FaultAbsorbedBy]);
    }

    // #989 FR-2 hygiene pin: a work item derived (scheduled) from a fault evaluation carries none of the three
    // fault-scoped metadata keys. Pins the fix shape directly, independent of the downstream misclassification.
    [Fact]
    public async Task ChildScheduleDerivedFromFaultEvaluation_CarriesNoFaultScopedMetadata()
    {
        var recorder = new EnqueuedWorkItemRecorder();
        await using var harness = WorkflowExecutionHarness.Create()
            .WithProbeLeaf()
            .WithFaultingLeaf()
            .ConfigureServices(services => services.AddSingleton(new FaultAbsorptionDirective()))
            .ConfigureServices(services => RecordingSchedulerWorkQueue.Register(services, recorder))
            .Build("actexec-parent", "actexec-f", "actexec-g");

        var run = await harness.RunAsync(NewReschedulingAbsorptionExecutable());
        run.AssertWorkflowCompleted();

        var scheduleItems = recorder.Items
            .Where(item => item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity)
            .ToArray();
        Assert.NotEmpty(scheduleItems);
        foreach (var item in scheduleItems)
        {
            Assert.DoesNotContain(RuntimeMetadataKeys.ChildFaulted, item.CommandMetadata.Keys);
            Assert.DoesNotContain(RuntimeMetadataKeys.IncidentId, item.CommandMetadata.Keys);
            Assert.DoesNotContain(RuntimeMetadataKeys.CompletedChildActivityExecutionId, item.CommandMetadata.Keys);
        }
    }

    // #989 latent leak in NewCompletionWorkItem: a composite that absorbs a child fault and COMPLETES under a
    // fault-handling grandparent must deliver its completion to the grandparent as a completion, not a fault.
    [Fact]
    public async Task CompositeAbsorbsAndCompletesUnderGrandparent_GrandparentReceivesCompletion()
    {
        var recorder = new ChildOutcomeRecorder();
        await using var harness = WorkflowExecutionHarness.Create()
            .WithProbeLeaf()
            .WithFaultingLeaf()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new FaultAbsorptionDirective { CompleteOnAbsorb = true });
                services.AddSingleton(recorder);
            })
            .Build("actexec-gp", "actexec-parent", "actexec-f");

        var run = await harness.RunAsync(StructuralExecutionTestSupport.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-gp", typeof(ChildOutcomeRecordingParentActivity),
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(FaultAbsorbingParentActivity),
                    WorkflowExecutionHarness.NewFaultingNode("node-f")))));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-gp");
        run.AssertCompleted("node-parent");
        Assert.Equal("completed", Assert.Single(recorder.Outcomes));
        var incident = Assert.Single(await ListIncidentsAsync(harness));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
    }

    private static WorkflowExecutionHarness ReschedulingHarness(FaultAbsorptionDirective directive) =>
        WorkflowExecutionHarness.Create()
            .WithProbeLeaf()
            .WithFaultingLeaf()
            .ConfigureServices(services => services.AddSingleton(directive))
            .Build("actexec-parent", "actexec-f", "actexec-g");

    private static WorkflowExecutable NewReschedulingAbsorptionExecutable() =>
        StructuralExecutionTestSupport.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(ReschedulingFaultAbsorbingParentActivity),
                WorkflowExecutionHarness.NewFaultingNode("node-f"),
                WorkflowExecutionHarness.NewProbeNode("node-g")));

    private static async Task<WorkflowExecutionRun> RunLeafFaultScenarioAsync(FaultAbsorptionDirective directive)
    {
        await using var harness = NewHarness(directive, "actexec-parent", "actexec-f", "actexec-a", "actexec-a-child");
        return await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(FaultAbsorbingParentActivity),
                    WorkflowExecutionHarness.NewFaultingNode("node-f"),
                    StructuralExecutionTestSupport.NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child")))),
            allowPendingWorkOnTerminalCompletion: true);
    }

    private static WorkflowExecutionHarness NewHarness(FaultAbsorptionDirective directive, params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithProbeLeaf()
            .WithFaultingLeaf()
            .ConfigureServices(services => services.AddSingleton(directive))
            .Build(activityExecutionIds);

    private static async Task<IReadOnlyCollection<IncidentState>> ListIncidentsAsync(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);

    /// <summary>What the parent under test should do when its child faults or completes.</summary>
    public sealed record FaultAbsorptionDirective(string Reason = "test-absorb")
    {
        public string? OverrideIncidentId { get; init; }
        public bool StageTwice { get; init; }
        public bool FaultInsteadOfContinue { get; init; }
        public bool CompleteOnAbsorb { get; init; }
        public bool AbsorbInCompletionEvaluation { get; init; }
    }

    /// <summary>
    /// Schedules all slot children; on a child fault it stages the directive's absorption and
    /// defers, completes, or faults per the directive. Child completions complete the composite
    /// (or illegally stage an absorption when the directive says so).
    /// </summary>
    public sealed class FaultAbsorbingParentActivity(FaultAbsorptionDirective directive) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityChildFaultHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            foreach (var child in Assert.Single(context.ExecutableNode.ChildSlots).Activities)
                context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
            var incidentId = directive.OverrideIncidentId ?? context.IncidentId!;
            runtimeContext.RequestChildFaultAbsorption(incidentId, directive.Reason);
            if (directive.StageTwice)
                runtimeContext.RequestChildFaultAbsorption(incidentId, directive.Reason);
            if (directive.FaultInsteadOfContinue)
                return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault("test.fault", "fault with staged absorption")));

            return ValueTask.FromResult(directive.CompleteOnAbsorb
                ? RuntimeStructuralContinuation.Complete()
                : RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            if (directive.AbsorbInCompletionEvaluation)
                ((IRuntimeActivityExecutionContext)context.ParentContext).RequestChildFaultAbsorption("incident-any", directive.Reason);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    /// <summary>Illegally stages a fault absorption during initial structural execution (spec 115 FR-2).</summary>
    public sealed class InitialAbsorptionStructuralActivity(FaultAbsorptionDirective directive) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            context.RequestChildFaultAbsorption("incident-any", directive.Reason);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }

    /// <summary>
    /// The #989 repro shape: schedules only its faulting child up front; on that child's fault it absorbs the
    /// incident AND schedules a fresh clean child during the SAME fault evaluation, then defers. The clean
    /// child's completion must arrive as an ordinary completion.
    /// </summary>
    public sealed class ReschedulingFaultAbsorbingParentActivity(FaultAbsorptionDirective directive) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityChildFaultHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var faultingChild = Assert.Single(context.ExecutableNode.ChildSlots).Activities.First();
            context.ScheduleChildActivity(faultingChild.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
            runtimeContext.RequestChildFaultAbsorption(context.IncidentId!, directive.Reason);
            var cleanChild = Assert.Single(runtimeContext.ExecutableNode.ChildSlots).Activities.Last();
            runtimeContext.ScheduleChildActivity(cleanChild.ExecutableNodeId, runtimeContext.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }

    /// <summary>Records whether a fault-handling parent's single child arrived as a completion or a fault (spec 132 grandparent chain).</summary>
    public sealed class ChildOutcomeRecorder
    {
        private readonly List<string> _outcomes = [];
        public IReadOnlyList<string> Outcomes => _outcomes;
        public void Record(string outcome) => _outcomes.Add(outcome);
    }

    /// <summary>
    /// A fault-handling structural parent that records the outcome its single child delivered. Completes on a
    /// completion; on a (mis)delivered fault it merely defers, so a leaked fault leaves a blocking incident and
    /// the workflow never completes — the negative signal the grandparent-chain test relies on.
    /// </summary>
    public sealed class ChildOutcomeRecordingParentActivity(ChildOutcomeRecorder recorder) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityChildFaultHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            recorder.Record("completed");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            recorder.Record("faulted");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
    }
}
