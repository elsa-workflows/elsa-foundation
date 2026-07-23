using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for spec 128 event subprocesses (tier 1: escalation + error triggers). Covers own-scope
/// sibling escalation (interrupting stops the other branch, non-interrupting runs both), child-thrown escalation
/// caught by the parent scope's event subprocess via seam C, the notification-side specificity ladder (boundary vs
/// event subprocess), error absorption via seam B, repeated non-interrupting activations, body fault → composite
/// fault, the scheduled-start seeding gap, and determinism.
/// </summary>
public sealed class BpmnEventSubprocessTests
{
    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(int idCount = 60) =>
        BpmnRuntimeFixture.CreateAsync(Ids(idCount), services => new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));

    private static string[] Ids(int count) =>
        new[] { "actexec-bpmn" }.Concat(Enumerable.Range(0, count).Select(index => $"aei-{index}")).ToArray();

    /// <summary>An event-subprocess body: an escalation/error start → a probe (<paramref name="probeNodeId"/>) → end.</summary>
    private static ExecutableNode EsBody(BpmnRuntimeFixture fixture, string nodeId, string probeNodeId, BpmnElement start) =>
        BpmnRuntimeFixture.NestedProcessNode(nodeId,
            elements: [start, BpmnRuntimeFixture.Task("es-task", childNodeId: probeNodeId), BpmnRuntimeFixture.EndEvent("es-end")],
            flows: [BpmnRuntimeFixture.Flow("es-f1", start.ElementId, "es-task"), BpmnRuntimeFixture.Flow("es-f2", "es-task", "es-end")],
            innerChildren: [fixture.NewProbeNode(probeNodeId)]);

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

    // ---- Own-scope sibling escalation --------------------------------------------------------------------------

    [Fact]
    public async Task OwnScope_Interrupting_StopsSiblingBranch_AndScopeCompletesAfterBody()
    {
        await using var fixture = await CreateFixtureAsync();
        var run = await fixture.RunAsync(OwnScopeExecutable(fixture, interrupting: true));

        // The throw branch fired the interrupting event subprocess before the sibling branch ran, so the sibling
        // probe never executed; the event-subprocess body ran and the scope completed.
        run.AssertWorkflowCompleted();
        Assert.Empty(run.States("node-sibling"));
        Assert.Single(run.States("node-es-probe"));
    }

    [Fact]
    public async Task OwnScope_NonInterrupting_RunsBothBranches()
    {
        await using var fixture = await CreateFixtureAsync();
        var run = await fixture.RunAsync(OwnScopeExecutable(fixture, interrupting: false));

        // Non-interrupting: the sibling branch and the event-subprocess body both ran to completion.
        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-sibling"));
        Assert.Single(run.States("node-es-probe"));
    }

    private static WorkflowExecutable OwnScopeExecutable(BpmnRuntimeFixture fixture, bool interrupting) =>
        fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-sibling"),
                EsBody(fixture, "node-esbody", "node-es-probe", BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "esc-1", interrupting))
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.EscalationThrow("throw", "esc-1"),
                BpmnRuntimeFixture.EndEvent("throw-end"),
                BpmnRuntimeFixture.Task("sibling", childNodeId: "node-sibling"),
                BpmnRuntimeFixture.EndEvent("sibling-end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-esbody")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "fork"),
                BpmnRuntimeFixture.Flow("flow-2", "fork", "throw"),
                BpmnRuntimeFixture.Flow("flow-3", "throw", "throw-end"),
                BpmnRuntimeFixture.Flow("flow-4", "fork", "sibling"),
                BpmnRuntimeFixture.Flow("flow-5", "sibling", "sibling-end")
            ]);

    // ---- Notification-side (seam C) escalation -----------------------------------------------------------------

    /// <summary>
    /// A nested process that forks a durable keeper (an Event suspended on the start evaluation — a committed-live
    /// descendant) and a trigger (an Event that, once resumed, raises escalation <paramref name="code"/>), so an
    /// interrupting parent catcher that fires on the trigger resume deterministically reclaims the keeper via seam A.
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
    public async Task ChildThrownEscalation_CaughtByParentEventSubprocess_Interrupting()
    {
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children:
            [
                NestedForkKeeperAndThrower("node-nested", "esc-1"),
                EsBody(fixture, "node-esbody", "node-es-probe", BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "esc-1", interrupting: true))
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-esbody")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
                BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-keep", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-trig", EventActivity.ResumeTargetId)
            ]);

        // The nested subprocess forks its keeper + trigger; no escalation raised yet.
        var run = await fixture.RunAsync(executable);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        var triggerBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-trig");

        // Resuming the trigger raises the escalation; the parent scope's interrupting event subprocess caught it via
        // seam C, tore the nested subprocess (and its committed-live keeper) down via seam A, ran the body, and completed.
        var resumed = await fixture.ResumeAsync(triggerBookmark, JsonSerializer.SerializeToElement(new EventReceived("trigger")));

        resumed.AssertWorkflowCompleted();
        Assert.Single(resumed.States("node-es-probe"));
        Assert.Equal(ActivityExecutionStatus.Cancelled, resumed.State("node-nested").Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, resumed.State("node-keep").Status);
        Assert.Empty(await fixture.BookmarksAsync());
    }

    [Fact]
    public async Task Ladder_BoundaryExact_BeatsEventSubprocessCatchAll()
    {
        await using var fixture = await CreateFixtureAsync();
        var run = await fixture.RunAsync(LadderExecutable(fixture,
            boundary: BpmnRuntimeFixture.EscalationBoundary("b-esc", "sub", "esc-1", cancelActivity: false),
            eventSubprocessStart: BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", code: null, interrupting: false)));

        // The host boundary's exact code beats the scope event subprocess's catch-all: the boundary fired, the ES did not.
        Assert.Single(run.States("node-bnd"));
        Assert.Empty(run.States("node-es-probe"));
    }

    [Fact]
    public async Task Ladder_EventSubprocessExact_BeatsBoundaryCatchAll()
    {
        await using var fixture = await CreateFixtureAsync();
        var run = await fixture.RunAsync(LadderExecutable(fixture,
            boundary: BpmnRuntimeFixture.EscalationBoundary("b-esc", "sub", code: null, cancelActivity: false),
            eventSubprocessStart: BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "esc-1", interrupting: false)));

        // The scope event subprocess's exact code beats the host boundary's catch-all: the ES activated, the boundary did not.
        Assert.Single(run.States("node-es-probe"));
        Assert.Empty(run.States("node-bnd"));
    }

    private static WorkflowExecutable LadderExecutable(BpmnRuntimeFixture fixture, BpmnElement boundary, BpmnElement eventSubprocessStart) =>
        fixture.NewExecutable(
            children:
            [
                NestedThrowThenWait("node-nested", "esc-1", "go"),
                fixture.NewProbeNode("node-bnd"),
                EsBody(fixture, "node-esbody", "node-es-probe", eventSubprocessStart)
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
                boundary,
                BpmnRuntimeFixture.Task("bnd-probe", childNodeId: "node-bnd"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EndEvent("bnd-end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-esbody")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
                BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end"),
                BpmnRuntimeFixture.Flow("flow-3", "b-esc", "bnd-probe"),
                BpmnRuntimeFixture.Flow("flow-4", "bnd-probe", "bnd-end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-nwait", EventActivity.ResumeTargetId)]);

    [Fact]
    public async Task UnmatchedEscalation_Bubbles_EventSubprocessDoesNotFire()
    {
        // The scope event subprocess is coded esc-OTHER; the nested throws esc-1. No match anywhere, and the parent
        // is the root, so the escalation is a diagnostic no-op — nothing fires and nothing faults.
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children:
            [
                NestedThrowThenWait("node-nested", "esc-1", "go"),
                EsBody(fixture, "node-esbody", "node-es-probe", BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "esc-OTHER", interrupting: false))
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-esbody")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
                BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-nwait", EventActivity.ResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        Assert.Empty(run.States("node-es-probe"));
        Assert.NotEqual(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
    }

    // ---- Error trigger -----------------------------------------------------------------------------------------

    // spec 132 (FR-4): the error event subprocess absorbs a host child fault via seam B and then activates its body —
    // a scheduled child, so the fault evaluation DEFERS. Executable since the runtime deferred-seam-B metadata-leak fix
    // (#989) landed: the named incident resolves, the scope's other live work is interrupted, the body runs, and the
    // scope completes normally.
    [Fact]
    public async Task ErrorEventSubprocess_AbsorbsChildFault_ResolvesIncident_RunsBody_ScopeCompletes()
    {
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewFaultingNode("node-fault"),
                EsBody(fixture, "node-esbody", "node-es-probe", BpmnRuntimeFixture.EventSubprocessErrorStart("es-start"))
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("work", childNodeId: "node-fault"),
                BpmnRuntimeFixture.EndEvent("end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-esbody")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "work"),
                BpmnRuntimeFixture.Flow("flow-2", "work", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        // The host child faults; the scope error event subprocess absorbed it, ran its body, and the scope completed.
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-fault").Status);
        Assert.Single(run.States("node-es-probe"));

        var incident = Assert.Single(await fixture.Provider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(IncidentResolutionAction.Continue, incident.ResolutionAction);
        Assert.NotNull(incident.ResolvedAt);
        Assert.Equal("actexec-bpmn", incident.Metadata[RuntimeMetadataKeys.FaultAbsorbedBy]);
        Assert.Equal(BpmnExecutionEngine.EventSubprocessErrorAbsorptionReason, incident.Metadata[RuntimeMetadataKeys.FaultAbsorptionReason]);
    }

    [Fact]
    public async Task EscalationEventSubprocess_DoesNotAbsorbChildFault_CompositeFaults()
    {
        // Only an ERROR event subprocess catches a child fault; a scope escalation event subprocess does not, so a
        // fault with no error catcher faults the composite exactly as without any event subprocess.
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "aei-0", "aei-1", "aei-2"]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewFaultingNode("node-fault"),
                EsBody(fixture, "node-esbody", "node-es-probe", BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "esc-1"))
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("work", childNodeId: "node-fault"),
                BpmnRuntimeFixture.EndEvent("end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-esbody")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "work"),
                BpmnRuntimeFixture.Flow("flow-2", "work", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(BpmnRuntimeFixture.ProcessNodeId).Status);
        Assert.Empty(run.States("node-es-probe"));
    }

    // ---- Repeated non-interrupting activations -----------------------------------------------------------------

    [Fact]
    public async Task RepeatedNonInterrupting_ActivatesBodyPerNotification()
    {
        await using var fixture = await CreateFixtureAsync(idCount: 80);
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
            children:
            [
                nested,
                EsBody(fixture, "node-esbody", "node-es-probe", BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "esc-1", interrupting: false))
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SubProcess("sub", childNodeId: "node-nested"),
                BpmnRuntimeFixture.EndEvent("host-end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-esbody")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub"),
                BpmnRuntimeFixture.Flow("flow-2", "sub", "host-end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-nwait", EventActivity.ResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        // One activation per escalation notification: the body ran twice.
        Assert.Equal(2, run.States("node-es-probe").Count);
    }

    // ---- Body fault --------------------------------------------------------------------------------------------

    [Fact]
    public async Task BodyFault_RidesCompositeFaultPath()
    {
        // An escalation-triggered event subprocess whose body itself faults (an event subprocess does not self-catch):
        // the fault rides the ordinary composite-fault path. Driven via an own-scope escalation (no seam-B), so the
        // body scheduling defers cleanly and the body fault surfaces as a composite fault.
        await using var fixture = await CreateFixtureAsync();
        var faultingBody = BpmnRuntimeFixture.NestedProcessNode("node-esbody",
            elements:
            [
                BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "esc-1"),
                BpmnRuntimeFixture.Task("es-task", childNodeId: "node-es-fault"),
                BpmnRuntimeFixture.EndEvent("es-end")
            ],
            flows: [BpmnRuntimeFixture.Flow("es-f1", "es-start", "es-task"), BpmnRuntimeFixture.Flow("es-f2", "es-task", "es-end")],
            innerChildren: [fixture.NewFaultingNode("node-es-fault")]);

        var executable = fixture.NewExecutable(
            children: [faultingBody],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EscalationThrow("throw", "esc-1"),
                BpmnRuntimeFixture.EndEvent("throw-end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-esbody")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "throw"),
                BpmnRuntimeFixture.Flow("flow-2", "throw", "throw-end")
            ]);

        var run = await fixture.RunAsync(executable);

        // The own-scope escalation activated the event subprocess, whose body then faulted → composite fault.
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(BpmnRuntimeFixture.ProcessNodeId).Status);
    }

    // ---- Scheduled-start seeding ------------------------------------------------------------------------------

    [Fact]
    public async Task Determinism_IdenticalRuns_ProduceIdenticalTokensAndDiagnostics()
    {
        var first = await RunOwnScopeInterruptingAsync();
        var second = await RunOwnScopeInterruptingAsync();

        Assert.Equal(
            first.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}:{token.Status}").OrderBy(value => value, StringComparer.Ordinal),
            second.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}:{token.Status}").OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            first.Diagnostics.Select(diagnostic => $"{diagnostic.DiagnosticId}:{diagnostic.Kind}").OrderBy(value => value, StringComparer.Ordinal),
            second.Diagnostics.Select(diagnostic => $"{diagnostic.DiagnosticId}:{diagnostic.Kind}").OrderBy(value => value, StringComparer.Ordinal));
    }

    private static async Task<BpmnExecutionState> RunOwnScopeInterruptingAsync()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.RunAsync(OwnScopeExecutable(fixture, interrupting: true));
        return await fixture.GetBpmnStateAsync();
    }
}
