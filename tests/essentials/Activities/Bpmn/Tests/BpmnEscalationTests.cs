using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for spec 127 escalation, riding runtime seam C: an escalation throw/end inside a nested
/// <c>BpmnProcess</c> signals upward; the escalation boundary on the hosting subprocess in the parent scope
/// catches it by code (specific beats catch-all), non-interrupting (nested keeps running) or interrupting (the
/// parent tears the nested process down via seam A); an unmatched escalation bubbles level by level; a
/// root-unmatched escalation is a no-op; late races fire non-interrupting boundaries but no-op interrupting ones.
/// </summary>
public sealed class BpmnEscalationTests
{
    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(int idCount = 40) =>
        BpmnRuntimeFixture.CreateAsync(Ids(idCount), services => new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));

    private static string[] Ids(int count) =>
        new[] { "actexec-bpmn" }.Concat(Enumerable.Range(0, count).Select(index => $"aei-{index}")).ToArray();

    /// <summary>A nested process that raises escalation <paramref name="code"/> from an intermediate throw, then suspends on an Event wait so the notification is delivered while the nested process is still a live child.</summary>
    private static ExecutableNode NestedThrowThenWait(string nodeId, string code, string waitEvent) =>
        BpmnRuntimeFixture.NestedProcessNode(nodeId,
            elements:
            [
                BpmnRuntimeFixture.StartEvent("n-start"),
                BpmnRuntimeFixture.EscalationThrow("n-throw", code),
                BpmnRuntimeFixture.Task("n-wait", childNodeId: "node-nwait"),
                BpmnRuntimeFixture.EndEvent("n-end")
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("n-flow-1", "n-start", "n-throw"),
                BpmnRuntimeFixture.Flow("n-flow-2", "n-throw", "n-wait"),
                BpmnRuntimeFixture.Flow("n-flow-3", "n-wait", "n-end")
            ],
            innerChildren: [BpmnRuntimeFixture.EventWaitNode("node-nwait", waitEvent)]);

    [Fact]
    public async Task NonInterrupting_FiresAlongsideRunningNestedProcess_BothPathsComplete()
    {
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children: [NestedThrowThenWait("node-nested", "esc-1", "go"), fixture.NewProbeNode("node-bnd")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
                BpmnRuntimeFixture.EscalationBoundary("b-esc", "sub", "esc-1", cancelActivity: false),
                BpmnRuntimeFixture.Task("bnd-probe", childNodeId: "node-bnd"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EndEvent("bnd-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
                BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end"),
                BpmnRuntimeFixture.Flow("flow-3", "b-esc", "bnd-probe"),
                BpmnRuntimeFixture.Flow("flow-4", "bnd-probe", "bnd-end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-nwait", EventActivity.ResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        // The non-interrupting boundary fired while the nested process kept running (still suspended on its wait).
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        Assert.Single(run.States("node-bnd"));
        var nestedWait = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-nwait");

        // Resume the nested wait: the nested process completes and the host routes its normal path — both paths done.
        var resumed = await fixture.ResumeAsync(nestedWait, JsonSerializer.SerializeToElement(new EventReceived("go")));
        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
        Assert.Single(resumed.States("node-bnd"));
    }

    /// <summary>
    /// A nested process that forks a durable keeper (an Event that stays suspended) and a trigger (an Event that,
    /// once resumed, raises escalation <paramref name="code"/>). The keeper suspends on the nested process's start
    /// evaluation — a committed live descendant — so an interrupting parent boundary that fires when the trigger
    /// is later resumed deterministically reclaims it through seam A.
    /// </summary>
    private static ExecutableNode NestedForkKeeperAndThrower(string nodeId, string code) =>
        BpmnRuntimeFixture.NestedProcessNode(nodeId,
            elements:
            [
                BpmnRuntimeFixture.StartEvent("n-start"),
                BpmnRuntimeFixture.ParallelGateway("n-fork"),
                BpmnRuntimeFixture.Task("n-keeper", childNodeId: "node-keep"),
                BpmnRuntimeFixture.EndEvent("n-keep-end"),
                BpmnRuntimeFixture.Task("n-trigger", childNodeId: "node-trig"),
                BpmnRuntimeFixture.EscalationThrow("n-throw", code),
                BpmnRuntimeFixture.EndEvent("n-throw-end")
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("n-flow-1", "n-start", "n-fork"),
                BpmnRuntimeFixture.Flow("n-flow-2", "n-fork", "n-keeper"),
                BpmnRuntimeFixture.Flow("n-flow-3", "n-keeper", "n-keep-end"),
                BpmnRuntimeFixture.Flow("n-flow-4", "n-fork", "n-trigger"),
                BpmnRuntimeFixture.Flow("n-flow-5", "n-trigger", "n-throw"),
                BpmnRuntimeFixture.Flow("n-flow-6", "n-throw", "n-throw-end")
            ],
            innerChildren:
            [
                BpmnRuntimeFixture.EventWaitNode("node-keep", "keep"),
                BpmnRuntimeFixture.EventWaitNode("node-trig", "trigger")
            ]);

    [Fact]
    public async Task Interrupting_TearsDownNestedProcess_AndRoutesBoundaryPath()
    {
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children: [NestedForkKeeperAndThrower("node-nested", "esc-1"), fixture.NewProbeNode("node-bnd")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
                BpmnRuntimeFixture.EscalationBoundary("b-esc", "sub", "esc-1", cancelActivity: true),
                BpmnRuntimeFixture.Task("bnd-probe", childNodeId: "node-bnd"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EndEvent("bnd-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
                BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end"),
                BpmnRuntimeFixture.Flow("flow-3", "b-esc", "bnd-probe"),
                BpmnRuntimeFixture.Flow("flow-4", "bnd-probe", "bnd-end")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-keep", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-trig", EventActivity.ResumeTargetId)
            ]);

        // The nested process suspends on both its keeper and its trigger; no escalation raised yet.
        var run = await fixture.RunAsync(executable);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        var triggerBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-trig");

        // Resuming the trigger raises the escalation; the interrupting boundary tears the nested process (and its
        // still-suspended keeper) down via seam A and routes the boundary path.
        var resumed = await fixture.ResumeAsync(triggerBookmark, JsonSerializer.SerializeToElement(new EventReceived("trigger")));

        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
        Assert.Single(resumed.States("node-bnd"));
        var nested = resumed.State("node-nested");
        Assert.Equal(ActivityExecutionStatus.Cancelled, nested.Status);
        Assert.Equal("ParentCancelled", nested.SubStatus);
        Assert.Equal(BpmnExecutionEngine.EscalationHostInterruptedReason, nested.Metadata[RuntimeMetadataKeys.ScopeCancellationReason]);
        // The nested process's still-suspended keeper child was reclaimed through seam A — no bookmarks remain.
        Assert.Equal(ActivityExecutionStatus.Cancelled, resumed.State("node-keep").Status);
        Assert.Empty(await fixture.BookmarksAsync());
    }

    /// <summary>A parent with a coded escalation boundary (+ a probe) and a code-less catch-all boundary (+ a probe) on the same subprocess host, plus a normal host-end path. Non-interrupting boundaries so the nested process keeps running.</summary>
    private WorkflowExecutable NewMatchingExecutable(BpmnRuntimeFixture fixture, string thrownCode, string boundaryCode)
    {
        var boundaries = boundaryCode == "*"
            ? new[]
            {
                BpmnRuntimeFixture.EscalationBoundary("b-specific", "sub", "esc-specific", cancelActivity: false),
                BpmnRuntimeFixture.EscalationBoundary("b-catchall", "sub", code: null, cancelActivity: false)
            }
            : [BpmnRuntimeFixture.EscalationBoundary("b-specific", "sub", boundaryCode, cancelActivity: false)];

        var elements = new List<BpmnElement>
        {
            BpmnRuntimeFixture.StartEvent(),
            BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
            BpmnRuntimeFixture.Task("probe-specific", childNodeId: "node-specific"),
            BpmnRuntimeFixture.EndEvent("host-end"),
            BpmnRuntimeFixture.EndEvent("end-specific")
        };
        elements.InsertRange(2, boundaries);

        var flows = new List<BpmnSequenceFlow>
        {
            BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
            BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end"),
            BpmnRuntimeFixture.Flow("flow-3", "b-specific", "probe-specific"),
            BpmnRuntimeFixture.Flow("flow-4", "probe-specific", "end-specific")
        };
        var children = new List<ExecutableNode> { NestedThrowThenWait("node-nested", thrownCode, "go"), fixture.NewProbeNode("node-specific") };
        if (boundaryCode == "*")
        {
            elements.Add(BpmnRuntimeFixture.Task("probe-catchall", childNodeId: "node-catchall"));
            elements.Add(BpmnRuntimeFixture.EndEvent("end-catchall"));
            flows.Add(BpmnRuntimeFixture.Flow("flow-5", "b-catchall", "probe-catchall"));
            flows.Add(BpmnRuntimeFixture.Flow("flow-6", "probe-catchall", "end-catchall"));
            children.Add(fixture.NewProbeNode("node-catchall"));
        }

        return fixture.NewExecutable(children, elements, flows,
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-nwait", EventActivity.ResumeTargetId)]);
    }

    [Fact]
    public async Task SpecificCode_BeatsCatchAll()
    {
        await using var fixture = await CreateFixtureAsync();
        var run = await fixture.RunAsync(NewMatchingExecutable(fixture, thrownCode: "esc-specific", boundaryCode: "*"));

        // The exact-code boundary fired; the catch-all did not.
        Assert.Single(run.States("node-specific"));
        Assert.Empty(run.States("node-catchall"));
    }

    [Fact]
    public async Task UnmatchedSpecificCode_FallsToCatchAll()
    {
        await using var fixture = await CreateFixtureAsync();
        var run = await fixture.RunAsync(NewMatchingExecutable(fixture, thrownCode: "esc-other", boundaryCode: "*"));

        // No exact code matched, so the code-less catch-all caught it.
        Assert.Empty(run.States("node-specific"));
        Assert.Single(run.States("node-catchall"));
    }

    [Fact]
    public async Task DistinctCode_NoMatch_BubblesToRoot_NoOp()
    {
        // The parent's only boundary is coded 'esc-A'; the nested process throws 'esc-B'. No match, and the parent
        // is the root, so it is a diagnostic no-op — the boundary never fires and nothing faults.
        await using var fixture = await CreateFixtureAsync();
        var run = await fixture.RunAsync(NewMatchingExecutable(fixture, thrownCode: "esc-B", boundaryCode: "esc-A"));

        Assert.Empty(run.States("node-specific"));
        Assert.NotEqual(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task RootProcessThrow_IsDiagnosticNoOp()
    {
        // An escalation thrown by a ROOT process (no parent scope) is a no-op: it routes onward and the process
        // completes normally, never faulting.
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-after")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EscalationThrow("throw", "esc-1"),
                BpmnRuntimeFixture.Task("after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent("end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "throw"),
                BpmnRuntimeFixture.Flow("flow-2", "throw", "after"),
                BpmnRuntimeFixture.Flow("flow-3", "after", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-after"));
    }

    [Fact]
    public async Task EscalationEndEvent_Variant_FiresBoundary()
    {
        // A nested process whose escalation is raised by an END event: it stages the notification and consumes the
        // token, so the nested process completes. A keeper branch keeps the parent Running so the (possibly late)
        // non-interrupting boundary still fires.
        await using var fixture = await CreateFixtureAsync();
        var nested = BpmnRuntimeFixture.NestedProcessNode("node-nested",
            elements:
            [
                BpmnRuntimeFixture.StartEvent("n-start"),
                BpmnRuntimeFixture.ParallelGateway("n-fork"),
                BpmnRuntimeFixture.EscalationEnd("n-esc-end", "esc-1"),
                BpmnRuntimeFixture.Task("n-keeper", childNodeId: "node-keep"),
                BpmnRuntimeFixture.EndEvent("n-keep-end")
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("n-flow-1", "n-start", "n-fork"),
                BpmnRuntimeFixture.Flow("n-flow-2", "n-fork", "n-esc-end"),
                BpmnRuntimeFixture.Flow("n-flow-3", "n-fork", "n-keeper"),
                BpmnRuntimeFixture.Flow("n-flow-4", "n-keeper", "n-keep-end")
            ],
            innerChildren: [BpmnRuntimeFixture.EventWaitNode("node-keep", "keep")]);

        var executable = fixture.NewExecutable(
            children: [nested, fixture.NewProbeNode("node-bnd")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
                BpmnRuntimeFixture.EscalationBoundary("b-esc", "sub", "esc-1", cancelActivity: false),
                BpmnRuntimeFixture.Task("bnd-probe", childNodeId: "node-bnd"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EndEvent("bnd-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
                BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end"),
                BpmnRuntimeFixture.Flow("flow-3", "b-esc", "bnd-probe"),
                BpmnRuntimeFixture.Flow("flow-4", "bnd-probe", "bnd-end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-keep", EventActivity.ResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        // The escalation-end escalation fired the non-interrupting boundary; the parent kept running on its keeper.
        Assert.Single(run.States("node-bnd"));
        Assert.NotEqual(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task TwoLevelBubbling_L3Throw_L2NoMatch_L1Catches()
    {
        // L3 (innermost) throws esc-1; L2 hosts L3 with NO escalation boundary, so it bubbles; L1 (root) catches it
        // on the boundary attached to its subprocess hosting L2.
        await using var fixture = await CreateFixtureAsync(idCount: 60);
        var l3 = NestedThrowThenWait("node-L3", "esc-1", "go");
        var l2 = BpmnRuntimeFixture.NestedProcessNode("node-L2",
            elements:
            [
                BpmnRuntimeFixture.StartEvent("l2-start"),
                BpmnRuntimeFixture.SubProcess("l2-sub", childNodeId: "node-L3"),
                BpmnRuntimeFixture.EndEvent("l2-end")
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("l2-flow-1", "l2-start", "l2-sub"),
                BpmnRuntimeFixture.Flow("l2-flow-2", "l2-sub", "l2-end")
            ],
            innerChildren: [l3]);

        var executable = fixture.NewExecutable(
            children: [l2, fixture.NewProbeNode("node-bnd")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-L2"),
                BpmnRuntimeFixture.EscalationBoundary("b-esc", "sub", "esc-1", cancelActivity: false),
                BpmnRuntimeFixture.Task("bnd-probe", childNodeId: "node-bnd"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EndEvent("bnd-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
                BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end"),
                BpmnRuntimeFixture.Flow("flow-3", "b-esc", "bnd-probe"),
                BpmnRuntimeFixture.Flow("flow-4", "bnd-probe", "bnd-end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-nwait", EventActivity.ResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        // The escalation bubbled L3 → L2 (no match) → L1, which fired its boundary.
        Assert.Single(run.States("node-bnd"));
        Assert.NotEqual(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task RepeatedNonInterrupting_FiresPerNotification()
    {
        // A nested process that throws twice (two intermediate throws with the same code) fires the non-interrupting
        // boundary twice — one boundary fire per notification is BPMN-conformant for non-interrupting.
        await using var fixture = await CreateFixtureAsync();
        var nested = BpmnRuntimeFixture.NestedProcessNode("node-nested",
            elements:
            [
                BpmnRuntimeFixture.StartEvent("n-start"),
                BpmnRuntimeFixture.EscalationThrow("n-throw-1", "esc-1"),
                BpmnRuntimeFixture.EscalationThrow("n-throw-2", "esc-1"),
                BpmnRuntimeFixture.Task("n-wait", childNodeId: "node-nwait"),
                BpmnRuntimeFixture.EndEvent("n-end")
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("n-flow-1", "n-start", "n-throw-1"),
                BpmnRuntimeFixture.Flow("n-flow-2", "n-throw-1", "n-throw-2"),
                BpmnRuntimeFixture.Flow("n-flow-3", "n-throw-2", "n-wait"),
                BpmnRuntimeFixture.Flow("n-flow-4", "n-wait", "n-end")
            ],
            innerChildren: [BpmnRuntimeFixture.EventWaitNode("node-nwait", "go")]);

        var executable = fixture.NewExecutable(
            children: [nested, fixture.NewProbeNode("node-bnd", outcomes: ["Done"])],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
                BpmnRuntimeFixture.EscalationBoundary("b-esc", "sub", "esc-1", cancelActivity: false),
                BpmnRuntimeFixture.Task("bnd-probe", childNodeId: "node-bnd"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EndEvent("bnd-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
                BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end"),
                BpmnRuntimeFixture.Flow("flow-3", "b-esc", "bnd-probe"),
                BpmnRuntimeFixture.Flow("flow-4", "bnd-probe", "bnd-end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-nwait", EventActivity.ResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        // The boundary fired once per escalation notification: the probe ran twice.
        Assert.Equal(2, run.States("node-bnd").Count);
    }

    [Fact]
    public async Task IdenticalRuns_ProduceIdenticalEscalationIds()
    {
        var first = await RunNonInterruptingAsync();
        var second = await RunNonInterruptingAsync();

        Assert.Equal(
            first.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}:{token.Status}").OrderBy(value => value, StringComparer.Ordinal),
            second.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}:{token.Status}").OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            first.Diagnostics.Select(diagnostic => $"{diagnostic.DiagnosticId}:{diagnostic.Kind}").OrderBy(value => value, StringComparer.Ordinal),
            second.Diagnostics.Select(diagnostic => $"{diagnostic.DiagnosticId}:{diagnostic.Kind}").OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task LateRace_NonInterrupting_FiresAfterHostCompletion()
    {
        // The nested process raises escalation from an END event and completes; a parallel keeper branch keeps the
        // parent Running. The non-interrupting boundary fires whether the notification arrives before or after the
        // host routed — additive semantics — and the host's own normal path also runs.
        await using var fixture = await CreateFixtureAsync();
        var nested = BpmnRuntimeFixture.NestedProcessNode("node-nested",
            elements: [BpmnRuntimeFixture.StartEvent("n-start"), BpmnRuntimeFixture.EscalationEnd("n-esc-end", "esc-1")],
            flows: [BpmnRuntimeFixture.Flow("n-flow-1", "n-start", "n-esc-end")]);

        var executable = fixture.NewExecutable(
            children: [nested, fixture.NewProbeNode("node-host"), fixture.NewProbeNode("node-bnd"), BpmnRuntimeFixture.EventWaitNode("node-keep", "keep")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
                BpmnRuntimeFixture.EscalationBoundary("b-esc", "sub", "esc-1", cancelActivity: false),
                BpmnRuntimeFixture.Task("host-after", childNodeId: "node-host"),
                BpmnRuntimeFixture.Task("keeper", childNodeId: "node-keep"),
                BpmnRuntimeFixture.Task("bnd-probe", childNodeId: "node-bnd"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EndEvent("keeper-end"),
                BpmnRuntimeFixture.EndEvent("bnd-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "fork"),
                BpmnRuntimeFixture.Flow("flow-2", "fork", "sub"),
                BpmnRuntimeFixture.Flow("flow-3", "sub", "host-after"),
                BpmnRuntimeFixture.Flow("flow-4", "host-after", "host-end"),
                BpmnRuntimeFixture.Flow("flow-5", "fork", "keeper"),
                BpmnRuntimeFixture.Flow("flow-6", "keeper", "keeper-end"),
                BpmnRuntimeFixture.Flow("flow-7", "b-esc", "bnd-probe"),
                BpmnRuntimeFixture.Flow("flow-8", "bnd-probe", "bnd-end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-keep", EventActivity.ResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        // The non-interrupting boundary fired, and the host's normal path also ran — both are additive.
        Assert.Single(run.States("node-bnd"));
        Assert.Single(run.States("node-host"));
        Assert.NotEqual(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task MultiInstanceHost_InstanceThrow_InterruptingCancelsCoordinator()
    {
        // A multi-instance subprocess host runs two nested-process instances (each forks a keeper + a trigger). One
        // instance's trigger is resumed, raising escalation; the interrupting boundary resolves the instance via
        // (node, iteration), cancels the loop coordinator, and the spec-121 cascade tears every instance down.
        await using var fixture = await CreateFixtureAsync(idCount: 80);
        var executable = fixture.NewExecutable(
            children: [NestedForkKeeperAndThrower("node-nested-mi", "esc-1"), fixture.NewProbeNode("node-bnd")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceSubProcess("mi-sub", "node-nested-mi", cardinality: 2, isSequential: false),
                BpmnRuntimeFixture.EscalationBoundary("b-esc", "mi-sub", "esc-1", cancelActivity: true),
                BpmnRuntimeFixture.Task("bnd-probe", childNodeId: "node-bnd"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EndEvent("bnd-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "mi-sub"),
                BpmnRuntimeFixture.Flow("flow-2", "mi-sub", "host-end"),
                BpmnRuntimeFixture.Flow("flow-3", "b-esc", "bnd-probe"),
                BpmnRuntimeFixture.Flow("flow-4", "bnd-probe", "bnd-end")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-keep", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-trig", EventActivity.ResumeTargetId)
            ]);

        // Both instances suspend on their keeper + trigger.
        var run = await fixture.RunAsync(executable);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        var triggerBookmark = (await fixture.BookmarksAsync()).First(candidate => candidate.ExecutableNodeId == "node-trig");

        // Resuming one instance's trigger raises escalation; the interrupting boundary cancels the coordinator, the
        // spec-121 cascade tears every instance down, and the boundary path routes.
        var resumed = await fixture.ResumeAsync(triggerBookmark, JsonSerializer.SerializeToElement(new EventReceived("trigger")));

        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
        Assert.Single(resumed.States("node-bnd"));
        // Every instance's suspended children were reclaimed — no bookmarks remain.
        Assert.Empty(await fixture.BookmarksAsync());
    }

    private static async Task<BpmnExecutionState> RunNonInterruptingAsync()
    {
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children: [NestedThrowThenWait("node-nested", "esc-1", "go"), fixture.NewProbeNode("node-bnd")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
                BpmnRuntimeFixture.EscalationBoundary("b-esc", "sub", "esc-1", cancelActivity: false),
                BpmnRuntimeFixture.Task("bnd-probe", childNodeId: "node-bnd"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EndEvent("bnd-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
                BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end"),
                BpmnRuntimeFixture.Flow("flow-3", "b-esc", "bnd-probe"),
                BpmnRuntimeFixture.Flow("flow-4", "bnd-probe", "bnd-end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-nwait", EventActivity.ResumeTargetId)]);
        await fixture.RunAsync(executable);
        return await fixture.GetBpmnStateAsync();
    }
}
