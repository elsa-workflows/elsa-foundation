using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for spec 134 event subprocesses (tier 2: message/signal/timer scope listeners). Covers arming
/// at scope start (the process still completes when real work ends — teardown-then-complete), non-interrupting fire +
/// re-arm (the repeat-fire pin), interrupting fire (other work + sibling listeners drained), timer repetition via
/// re-arm, deadlock-detector consistency under the filtered liveness view, and interplay (terminate / interrupting
/// escalation drains tier-2 listeners) + determinism.
/// </summary>
public sealed class BpmnEventSubprocessTier2Tests
{
    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(int idCount = 120) =>
        F.CreateAsync(Ids(idCount), services => new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));

    private static string[] Ids(int count) =>
        new[] { "actexec-bpmn" }.Concat(Enumerable.Range(0, count).Select(index => $"aei-{index}")).ToArray();

    /// <summary>A message/signal/timer event-subprocess body: an external-trigger start → a probe → end.</summary>
    private static ExecutableNode EsBody(BpmnRuntimeFixture fixture, string nodeId, string probeNodeId, BpmnElement start) =>
        F.NestedProcessNode(nodeId,
            elements: [start, F.Task("es-task", childNodeId: probeNodeId), F.EndEvent("es-end")],
            flows: [F.Flow("es-f1", start.ElementId, "es-task"), F.Flow("es-f2", "es-task", "es-end")],
            innerChildren: [fixture.NewProbeNode(probeNodeId)]);

    // ---- Arming at scope start + teardown-then-complete ---------------------------------------------------------

    [Fact]
    public async Task ArmedListener_DoesNotBlockCompletion_TearsDownWhenRealWorkEnds()
    {
        await using var fixture = await CreateFixtureAsync();
        var run = await fixture.RunAsync(TeardownExecutable(fixture, interrupting: true));

        // The scope listener armed at start suspends alongside the real "hold" wait; the process is still running.
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        var bookmarks = await fixture.BookmarksAsync();
        Assert.Contains(bookmarks, b => b.ExecutableNodeId == "node-es-listener");
        Assert.Contains(bookmarks, b => b.ExecutableNodeId == "node-hold");

        // Ending the real work completes the scope: the still-armed durable listener child is cancelled and its
        // bookmark is gone (the teardown-then-complete pin).
        var hold = Assert.Single(bookmarks, b => b.ExecutableNodeId == "node-hold");
        var resumed = await fixture.ResumeAsync(hold, JsonSerializer.SerializeToElement(new EventReceived("hold")));

        resumed.AssertWorkflowCompleted();
        Assert.Empty(resumed.States("node-es-probe"));
        Assert.Equal(ActivityExecutionStatus.Cancelled, resumed.State("node-es-listener").Status);
        Assert.Empty(await fixture.BookmarksAsync());
    }

    private static WorkflowExecutable TeardownExecutable(BpmnRuntimeFixture fixture, bool interrupting) =>
        fixture.NewExecutable(
            children:
            [
                EsBody(fixture, "node-esbody", "node-es-probe", F.EventSubprocessMessageStart("es-start", "esmsg", interrupting)),
                F.EventWaitNode("node-es-listener", "esmsg"),
                F.EventWaitNode("node-hold", "hold")
            ],
            elements:
            [
                F.StartEvent(),
                F.Task("hold", childNodeId: "node-hold"),
                F.EndEvent("end"),
                F.EventSubprocessWithListener("es", "node-esbody", "node-es-listener")
            ],
            sequenceFlows:
            [
                F.Flow("flow-1", "start", "hold"),
                F.Flow("flow-2", "hold", "end")
            ],
            resumeTargets:
            [
                F.NodeResumeTarget("node-hold", EventActivity.ResumeTargetId),
                F.NodeResumeTarget("node-es-listener", EventActivity.ResumeTargetId)
            ]);

    [Fact]
    public async Task ArmedListener_WithNoOtherWork_CompletesImmediately()
    {
        // The degenerate case: real work (start → end) finishes synchronously in the SAME evaluation the listener is
        // armed. The scope must still complete (BPMN semantics: the listener never gets a chance to fire).
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children:
            [
                EsBody(fixture, "node-esbody", "node-es-probe", F.EventSubprocessMessageStart("es-start", "esmsg")),
                F.EventWaitNode("node-es-listener", "esmsg")
            ],
            elements:
            [
                F.StartEvent(),
                F.EndEvent("end"),
                F.EventSubprocessWithListener("es", "node-esbody", "node-es-listener")
            ],
            sequenceFlows: [F.Flow("flow-1", "start", "end")],
            resumeTargets: [F.NodeResumeTarget("node-es-listener", EventActivity.ResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        run.AssertWorkflowCompleted();
        Assert.Empty(await fixture.BookmarksAsync());
    }

    // ---- Non-interrupting fire + re-arm (the repeat-fire pin) ---------------------------------------------------

    [Fact]
    public async Task MessageNonInterrupting_FiresTwice_ViaReArm()
    {
        await using var fixture = await CreateFixtureAsync();
        var executable = TeardownExecutable(fixture, interrupting: false);

        var run = await fixture.RunAsync(executable);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);

        // First stimulus: the listener fires, the body runs, and a fresh listener re-arms.
        var firstListener = Assert.Single(await fixture.BookmarksAsync(), b => b.ExecutableNodeId == "node-es-listener");
        var afterFirst = await fixture.ResumeAsync(firstListener, JsonSerializer.SerializeToElement(new EventReceived("esmsg")));
        Assert.Equal(WorkflowExecutionStatus.Running, afterFirst.WorkflowState?.Status);
        Assert.Single(afterFirst.States("node-es-probe"));

        // Second stimulus: the re-armed listener fires again — the body runs a SECOND time (repetition via re-arm).
        var secondListener = Assert.Single(await fixture.BookmarksAsync(), b => b.ExecutableNodeId == "node-es-listener");
        var afterSecond = await fixture.ResumeAsync(secondListener, JsonSerializer.SerializeToElement(new EventReceived("esmsg")));
        Assert.Equal(2, afterSecond.States("node-es-probe").Count);

        // Ending the real work tears the (re-armed) listener down and completes the scope.
        var hold = Assert.Single(await fixture.BookmarksAsync(), b => b.ExecutableNodeId == "node-hold");
        var completed = await fixture.ResumeAsync(hold, JsonSerializer.SerializeToElement(new EventReceived("hold")));
        completed.AssertWorkflowCompleted();
    }

    // ---- Interrupting fire (other work + sibling listeners drained) ---------------------------------------------

    [Fact]
    public async Task MessageInterrupting_DrainsOtherWorkAndSiblingListeners_BodyRuns_ScopeCompletes()
    {
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children:
            [
                EsBody(fixture, "node-esbody", "node-es-probe", F.EventSubprocessMessageStart("es-start", "esmsg", interrupting: true)),
                F.EventWaitNode("node-es-listener", "esmsg"),
                // A sibling signal listener that must be drained by the interrupting fire.
                EsBody(fixture, "node-esbody2", "node-es-probe2", F.EventSubprocessSignalStart("es2-start", "essig")),
                F.EventWaitNode("node-es2-listener", "essig"),
                F.EventWaitNode("node-hold", "hold")
            ],
            elements:
            [
                F.StartEvent(),
                F.Task("hold", childNodeId: "node-hold"),
                F.EndEvent("end"),
                F.EventSubprocessWithListener("es", "node-esbody", "node-es-listener"),
                F.EventSubprocessWithListener("es2", "node-esbody2", "node-es2-listener")
            ],
            sequenceFlows:
            [
                F.Flow("flow-1", "start", "hold"),
                F.Flow("flow-2", "hold", "end")
            ],
            resumeTargets:
            [
                F.NodeResumeTarget("node-hold", EventActivity.ResumeTargetId),
                F.NodeResumeTarget("node-es-listener", EventActivity.ResumeTargetId),
                F.NodeResumeTarget("node-es2-listener", EventActivity.ResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);

        // The interrupting message listener fires: the "hold" real work AND the sibling signal listener are drained;
        // the body runs; the scope completes without needing the hold to resume.
        var listener = Assert.Single(await fixture.BookmarksAsync(), b => b.ExecutableNodeId == "node-es-listener");
        var resumed = await fixture.ResumeAsync(listener, JsonSerializer.SerializeToElement(new EventReceived("esmsg")));

        resumed.AssertWorkflowCompleted();
        Assert.Single(resumed.States("node-es-probe"));
        Assert.Empty(resumed.States("node-es-probe2"));
        Assert.Equal(ActivityExecutionStatus.Cancelled, resumed.State("node-hold").Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, resumed.State("node-es2-listener").Status);
        Assert.Empty(await fixture.BookmarksAsync());
    }

    // ---- Timer-triggered repetition via re-arm ------------------------------------------------------------------

    [Fact]
    public async Task TimerNonInterrupting_RepeatsViaReArm()
    {
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children:
            [
                EsBody(fixture, "node-esbody", "node-es-probe", F.EventSubprocessTimerStart("es-start", "PT5M", interrupting: false)),
                F.DelayWaitNode("node-es-listener", TimeSpan.FromMinutes(5)),
                F.EventWaitNode("node-hold", "hold")
            ],
            elements:
            [
                F.StartEvent(),
                F.Task("hold", childNodeId: "node-hold"),
                F.EndEvent("end"),
                F.EventSubprocessWithListener("es", "node-esbody", "node-es-listener")
            ],
            sequenceFlows:
            [
                F.Flow("flow-1", "start", "hold"),
                F.Flow("flow-2", "hold", "end")
            ],
            resumeTargets:
            [
                F.NodeResumeTarget("node-hold", EventActivity.ResumeTargetId),
                F.NodeResumeTarget("node-es-listener", "resume-target:durable-timer")
            ]);

        var run = await fixture.RunAsync(executable);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);

        // First timer fire.
        var firstTimer = Assert.Single(await fixture.BookmarksAsync(), b => b.ExecutableNodeId == "node-es-listener");
        var afterFirst = await fixture.ResumeAsync(firstTimer, JsonSerializer.SerializeToElement(new DurableTimerElapsed(firstTimer.StimulusHash)));
        Assert.Single(afterFirst.States("node-es-probe"));

        // Second timer fire via the re-armed one-shot Delay.
        var secondTimer = Assert.Single(await fixture.BookmarksAsync(), b => b.ExecutableNodeId == "node-es-listener");
        var afterSecond = await fixture.ResumeAsync(secondTimer, JsonSerializer.SerializeToElement(new DurableTimerElapsed(secondTimer.StimulusHash)));
        Assert.Equal(2, afterSecond.States("node-es-probe").Count);
    }

    // ---- Deadlock-detector consistency under the filtered view --------------------------------------------------

    [Fact]
    public async Task DeadlockDetector_RealJoinWaiter_StillFaults_WithListenerArmed()
    {
        // An exclusive decision routes to only one branch, but both branches reconverge at a parallel join: the
        // missing arrival can never be produced. A scope listener is armed alongside — the filtered deadlock detector
        // excludes the listener but MUST still fault bpmn.join.deadlock for the genuine join-waiter (the pin: patching
        // the completion check alone would make the detector misfire; the two predicates are filtered together).
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-decision", outcomes: ["A"]),
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                EsBody(fixture, "node-esbody", "node-es-probe", F.EventSubprocessMessageStart("es-start", "esmsg")),
                F.EventWaitNode("node-es-listener", "esmsg")
            ],
            elements:
            [
                F.StartEvent(),
                F.ExclusiveGateway("decision", childNodeId: "node-decision"),
                F.Task("task-a", childNodeId: "node-a"),
                F.Task("task-b", childNodeId: "node-b"),
                F.ParallelGateway("join"),
                F.EndEvent("end"),
                F.EventSubprocessWithListener("es", "node-esbody", "node-es-listener")
            ],
            sequenceFlows:
            [
                F.Flow("flow-1", "start", "decision"),
                F.Flow("flow-a", "decision", "task-a", conditionOutcome: "A"),
                F.Flow("flow-b", "decision", "task-b", conditionOutcome: "B"),
                F.Flow("flow-2", "task-a", "join"),
                F.Flow("flow-3", "task-b", "join"),
                F.Flow("flow-4", "join", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(F.ProcessNodeId).Status);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    // ---- Interplay: interrupting escalation drains tier-2 listeners ---------------------------------------------

    [Fact]
    public async Task InterruptingEscalation_DrainsTier2Listeners()
    {
        // An own-scope interrupting escalation event subprocess fires; its stop-others must drain the tier-2 message
        // scope listener (an ordinary live token there).
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children:
            [
                F.NestedProcessNode("node-escbody",
                    elements: [F.EventSubprocessEscalationStart("esc-start", "esc-1"), F.Task("esc-task", childNodeId: "node-esc-probe"), F.EndEvent("esc-end")],
                    flows: [F.Flow("esc-f1", "esc-start", "esc-task"), F.Flow("esc-f2", "esc-task", "esc-end")],
                    innerChildren: [fixture.NewProbeNode("node-esc-probe")]),
                EsBody(fixture, "node-esbody", "node-es-probe", F.EventSubprocessMessageStart("es-start", "esmsg")),
                F.EventWaitNode("node-es-listener", "esmsg")
            ],
            elements:
            [
                F.StartEvent(),
                F.EscalationThrow("throw", "esc-1"),
                F.EndEvent("throw-end"),
                F.EventSubprocess("esc", "node-escbody"),
                F.EventSubprocessWithListener("es", "node-esbody", "node-es-listener")
            ],
            sequenceFlows:
            [
                F.Flow("flow-1", "start", "throw"),
                F.Flow("flow-2", "throw", "throw-end")
            ]);

        var run = await fixture.RunAsync(executable);

        // The interrupting escalation event subprocess ran; the tier-2 listener was drained; the scope completed.
        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-esc-probe"));
        Assert.Empty(run.States("node-es-probe"));
        Assert.Empty(await fixture.BookmarksAsync());
    }

    // ---- Interplay: terminate kills listeners ------------------------------------------------------------------

    [Fact]
    public async Task Terminate_KillsArmedListener()
    {
        // A terminate end event reached after the listener is armed must tear the listener down (the unfiltered
        // CancelLiveWork teardown site) and end the process; the listener never fires.
        await using var fixture = await CreateFixtureAsync();
        var executable = fixture.NewExecutable(
            children:
            [
                EsBody(fixture, "node-esbody", "node-es-probe", F.EventSubprocessMessageStart("es-start", "esmsg")),
                F.EventWaitNode("node-es-listener", "esmsg"),
                F.EventWaitNode("node-go", "go")
            ],
            elements:
            [
                F.StartEvent(),
                F.Task("go", childNodeId: "node-go"),
                F.TerminateEndEvent("term"),
                F.EventSubprocessWithListener("es", "node-esbody", "node-es-listener")
            ],
            sequenceFlows:
            [
                F.Flow("flow-1", "start", "go"),
                F.Flow("flow-2", "go", "term")
            ],
            resumeTargets:
            [
                F.NodeResumeTarget("node-go", EventActivity.ResumeTargetId),
                F.NodeResumeTarget("node-es-listener", EventActivity.ResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);

        var go = Assert.Single(await fixture.BookmarksAsync(), b => b.ExecutableNodeId == "node-go");
        var terminated = await fixture.ResumeAsync(go, JsonSerializer.SerializeToElement(new EventReceived("go")));

        // The terminate ended the process; the listener token was drained by the unfiltered CancelLiveWork, so the
        // listener never fired (the body did not run). A suspended child's bookmark lingers exactly as for any
        // terminated suspended child — the terminate logical-only precedent.
        terminated.AssertWorkflowCompleted();
        Assert.Empty(terminated.States("node-es-probe"));
    }

    // ---- Determinism -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Determinism_IdenticalRuns_ProduceIdenticalTokensAndDiagnostics()
    {
        var first = await RunReArmedAsync();
        var second = await RunReArmedAsync();

        Assert.Equal(
            first.Tokens.Select(t => $"{t.TokenId}:{t.AtElementId}:{t.ParentTokenId}:{t.Status}:{t.Kind}").OrderBy(v => v, StringComparer.Ordinal),
            second.Tokens.Select(t => $"{t.TokenId}:{t.AtElementId}:{t.ParentTokenId}:{t.Status}:{t.Kind}").OrderBy(v => v, StringComparer.Ordinal));
        Assert.Equal(
            first.Diagnostics.Select(d => $"{d.DiagnosticId}:{d.Kind}").OrderBy(v => v, StringComparer.Ordinal),
            second.Diagnostics.Select(d => $"{d.DiagnosticId}:{d.Kind}").OrderBy(v => v, StringComparer.Ordinal));
    }

    private static async Task<BpmnExecutionState> RunReArmedAsync()
    {
        await using var fixture = await CreateFixtureAsync();
        var executable = TeardownExecutable(fixture, interrupting: false);
        await fixture.RunAsync(executable);
        var listener = Assert.Single(await fixture.BookmarksAsync(), b => b.ExecutableNodeId == "node-es-listener");
        await fixture.ResumeAsync(listener, JsonSerializer.SerializeToElement(new EventReceived("esmsg")));
        return await fixture.GetBpmnStateAsync();
    }
}
