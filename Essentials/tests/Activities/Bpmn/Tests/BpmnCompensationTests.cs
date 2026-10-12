using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for spec 124 compensation: a compensation boundary registers a host's successful
/// completion in a reverse-order log; a compensate throw/end replays the registered handlers most-recently-first,
/// one at a time; <c>activityRef</c> targets one host; an empty log is a no-op; a cycle registers per pass; a
/// handler fault faults the composite; concurrent throws claim disjoint targets; and the comp:/comprun:/token id
/// stream is deterministic.
/// </summary>
public sealed class BpmnCompensationTests
{
    private const string DelayResumeTargetId = "resume-target:durable-timer";

    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(int idCount, bool withScheduling = false) =>
        BpmnRuntimeFixture.CreateAsync(Ids(idCount), services =>
        {
            if (withScheduling)
                new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);
        });

    private static ValueTask<BpmnRuntimeFixture> CreateLoopFixtureAsync(BpmnLoopDecisionCounter counter, int idCount) =>
        BpmnRuntimeFixture.CreateAsync(Ids(idCount), services => services.AddSingleton(counter));

    private static string[] Ids(int count) =>
        new[] { "actexec-bpmn" }.Concat(Enumerable.Range(0, count).Select(index => $"aei-{index}")).ToArray();

    [Fact]
    public async Task RefLessThrow_ReplaysHandlersInReverseCompletionOrder_OneAtATime()
    {
        // start → hostA → hostB → throw → end. hostA then hostB complete (register compA then compB). The ref-less
        // throw replays newest-first: handlerB first, then handlerA — pinned by suspending Delay handlers resumed
        // one at a time (never two live at once).
        await using var fixture = await CreateFixtureAsync(idCount: 30, withScheduling: true);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-hostA"), fixture.NewProbeNode("node-hostB"),
                NewDelayNode("node-hA"), NewDelayNode("node-hB")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("hostA", childNodeId: "node-hostA"),
                BpmnRuntimeFixture.Task("hostB", childNodeId: "node-hostB"),
                BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-hA"),
                BpmnRuntimeFixture.CompensationHandler("handlerB", childNodeId: "node-hB"),
                BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA"),
                BpmnRuntimeFixture.CompensationBoundary("cb-b", "hostB", "handlerB"),
                BpmnRuntimeFixture.CompensateThrow("throw"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "hostA"),
                BpmnRuntimeFixture.Flow("flow-2", "hostA", "hostB"),
                BpmnRuntimeFixture.Flow("flow-3", "hostB", "throw"),
                BpmnRuntimeFixture.Flow("flow-4", "throw", "end")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-hA", DelayResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-hB", DelayResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);

        // handlerB runs first (most-recently-completed host), alone.
        var firstBookmark = Assert.Single(await fixture.BookmarksAsync());
        Assert.Equal("node-hB", firstBookmark.ExecutableNodeId);
        run = await fixture.ResumeAsync(firstBookmark, JsonSerializer.SerializeToElement(new DurableTimerElapsed(firstBookmark.StimulusHash)));

        // then handlerA, alone.
        var secondBookmark = Assert.Single(await fixture.BookmarksAsync());
        Assert.Equal("node-hA", secondBookmark.ExecutableNodeId);
        run = await fixture.ResumeAsync(secondBookmark, JsonSerializer.SerializeToElement(new DurableTimerElapsed(secondBookmark.StimulusHash)));

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        // Both handlers ran exactly once, most-recently-first (order pinned by the bookmark sequence above).
        Assert.Single(run.States("node-hA"));
        Assert.Single(run.States("node-hB"));
    }

    [Fact]
    public async Task ReplayLifecycle_RegisteredThenClaimedThenCompensated_ObservableWhileAlive()
    {
        // start → hostA(probe) → throw → waiter(Event, never resumed) → end. Once the replay finishes the throw
        // routes to the waiter and the process suspends alive, so the live state shows the compensable Compensated
        // and no run remaining (the transition Registered → Claimed → Compensated ran to completion).
        await using var fixture = await CreateFixtureAsync(idCount: 20, withScheduling: true);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-hostA"), fixture.NewProbeNode("node-hA"), NewEventWaitNode("node-hold", "hold")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("hostA", childNodeId: "node-hostA"),
                BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-hA"),
                BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA"),
                BpmnRuntimeFixture.CompensateThrow("throw"),
                BpmnRuntimeFixture.Task("waiter", childNodeId: "node-hold"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "hostA"),
                BpmnRuntimeFixture.Flow("flow-2", "hostA", "throw"),
                BpmnRuntimeFixture.Flow("flow-3", "throw", "waiter"),
                BpmnRuntimeFixture.Flow("flow-4", "waiter", "end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-hold", Event.ResumeTargetId)]);

        await fixture.RunAsync(executable);

        var state = await fixture.GetBpmnStateAsync();
        Assert.Equal(BpmnCompensableStatus.Compensated, Assert.Single(state.Compensables).Status);
        Assert.Empty(state.CompensationRuns);
    }

    [Fact]
    public async Task ActivityRefThrow_CompensatesOnlyTheReferencedHost()
    {
        // A ref throw targeting hostA compensates only compA; hostB's handler never runs.
        await using var fixture = await CreateFixtureAsync(idCount: 30);
        var run = await fixture.RunAsync(NewTwoHostExecutable(fixture, BpmnRuntimeFixture.CompensateThrow("throw", activityRef: "hostA")));

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-hA"));
        Assert.Empty(run.States("node-hB"));
    }

    [Fact]
    public async Task EmptyLog_Throw_CompletesImmediately_NoHandlerRuns_NoRun()
    {
        // The throw targets hostA, which carries a compensation boundary but is never reached (no flows), so nothing
        // is registered → the throw completes immediately (routes), with no handler run and no run record.
        await using var fixture = await CreateFixtureAsync(idCount: 10);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-hostA"), fixture.NewProbeNode("node-hA")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.CompensateThrow("throw", activityRef: "hostA"),
                BpmnRuntimeFixture.EndEvent(),
                BpmnRuntimeFixture.Task("hostA", childNodeId: "node-hostA"),
                BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-hA"),
                BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "throw"),
                BpmnRuntimeFixture.Flow("flow-2", "throw", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        // Nothing was claimed and no handler ran: the throw routed straight through (no fault, no suspension).
        Assert.Empty(run.States("node-hA"));
    }

    [Fact]
    public async Task CompensateEnd_ReplaysThenConsumesToken()
    {
        await using var fixture = await CreateFixtureAsync(idCount: 30);
        var run = await fixture.RunAsync(NewTwoHostExecutable(fixture, BpmnRuntimeFixture.CompensateEnd("end"), routeThrowToEnd: false));

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-hA"));
        Assert.Single(run.States("node-hB"));
    }

    [Fact]
    public async Task Cycle_RegistersPerCompletion_AndReplaysEachRegistration()
    {
        // start → hostA → gw ; gw --again--> hostA (loop back) ; gw --exit--> throw → end.
        // hostA completes twice (two registrations); the ref-less throw compensates both (handlerA runs twice).
        var counter = new BpmnLoopDecisionCounter { LoopBackCount = 1 }; // hostA runs twice
        await using var fixture = await CreateLoopFixtureAsync(counter, idCount: 30);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-hostA"), NewDecisionNode("node-decision"), fixture.NewProbeNode("node-hA")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("hostA", childNodeId: "node-hostA"),
                BpmnRuntimeFixture.ExclusiveGateway("gw", childNodeId: "node-decision"),
                BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-hA"),
                BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA"),
                BpmnRuntimeFixture.CompensateThrow("throw"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "hostA"),
                BpmnRuntimeFixture.Flow("flow-2", "hostA", "gw"),
                BpmnRuntimeFixture.Flow("again", "gw", "hostA", conditionOutcome: BpmnLoopDecisionActivity.LoopBack),
                BpmnRuntimeFixture.Flow("exit", "gw", "throw", isDefault: true),
                BpmnRuntimeFixture.Flow("flow-3", "throw", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        var state = await fixture.GetBpmnStateAsync();
        // Two per-completion registrations, both for hostA; the ref-less throw replayed both (handler ran twice).
        Assert.Equal(2, state.Compensables.Count);
        Assert.All(state.Compensables, compensable => Assert.Equal("hostA", compensable.HostElementId));
        Assert.Equal(2, run.States("node-hA").Count);
    }

    [Fact]
    public async Task HandlerFault_FaultsComposite()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-host", "actexec-handler"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-host"), fixture.NewFaultingNode("node-h")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("hostA", childNodeId: "node-host"),
                BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-h"),
                BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA"),
                BpmnRuntimeFixture.CompensateThrow("throw"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "hostA"),
                BpmnRuntimeFixture.Flow("flow-2", "hostA", "throw"),
                BpmnRuntimeFixture.Flow("flow-3", "throw", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(BpmnRuntimeFixture.ProcessNodeId).Status);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task ConcurrentThrows_ClaimDisjointTargets_EachHandlerRunsExactlyOnce()
    {
        // start → hostA → hostB → fork ; fork → throwA(ref hostA), fork → throwB(ref hostB) ; both → join → end.
        // Two concurrent throws claim disjoint compensables (each its own host), so each handler runs exactly once.
        await using var fixture = await CreateFixtureAsync(idCount: 30);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-hostA"), fixture.NewProbeNode("node-hostB"),
                fixture.NewProbeNode("node-hA"), fixture.NewProbeNode("node-hB")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("hostA", childNodeId: "node-hostA"),
                BpmnRuntimeFixture.Task("hostB", childNodeId: "node-hostB"),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-hA"),
                BpmnRuntimeFixture.CompensationHandler("handlerB", childNodeId: "node-hB"),
                BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA"),
                BpmnRuntimeFixture.CompensationBoundary("cb-b", "hostB", "handlerB"),
                BpmnRuntimeFixture.CompensateThrow("throwA", activityRef: "hostA"),
                BpmnRuntimeFixture.CompensateThrow("throwB", activityRef: "hostB"),
                BpmnRuntimeFixture.ParallelGateway("join"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "hostA"),
                BpmnRuntimeFixture.Flow("flow-2", "hostA", "hostB"),
                BpmnRuntimeFixture.Flow("flow-3", "hostB", "fork"),
                BpmnRuntimeFixture.Flow("flow-4", "fork", "throwA"),
                BpmnRuntimeFixture.Flow("flow-5", "fork", "throwB"),
                BpmnRuntimeFixture.Flow("flow-6", "throwA", "join"),
                BpmnRuntimeFixture.Flow("flow-7", "throwB", "join"),
                BpmnRuntimeFixture.Flow("flow-8", "join", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        // Disjoint claims: each throw compensated only its own host's registration, so each handler ran exactly once.
        Assert.Single(run.States("node-hA"));
        Assert.Single(run.States("node-hB"));
    }

    [Fact]
    public async Task Terminate_MidReplay_StaysLogicalOnly_RunAndClaimsRetained()
    {
        // start → hostA → fork ; fork → throw(handler suspends on Delay), fork → wait("kill") → terminate.
        // The terminate fires while the run's handler is suspended: CancelLiveWork is logical-only, so the run
        // record survives and its compensable stays Claimed (never Compensated, never released to Registered).
        await using var fixture = await CreateFixtureAsync(idCount: 30, withScheduling: true);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-hostA"), NewDelayNode("node-hA"),
                NewEventWaitNode("node-kill", "kill")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("hostA", childNodeId: "node-hostA"),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-hA"),
                BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA"),
                BpmnRuntimeFixture.CompensateThrow("throw"),
                BpmnRuntimeFixture.Task("waiter", childNodeId: "node-kill"),
                BpmnRuntimeFixture.TerminateEndEvent("terminate"),
                BpmnRuntimeFixture.EndEvent("end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "hostA"),
                BpmnRuntimeFixture.Flow("flow-2", "hostA", "fork"),
                BpmnRuntimeFixture.Flow("flow-3", "fork", "throw"),
                BpmnRuntimeFixture.Flow("flow-4", "throw", "end"),
                BpmnRuntimeFixture.Flow("flow-5", "fork", "waiter"),
                BpmnRuntimeFixture.Flow("flow-6", "waiter", "terminate")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-hA", DelayResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-kill", Elsa.Activities.Primitives.Activities.Event.ResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);

        // The compensation run is live (handler suspended) alongside the "kill" waiter.
        var stateBefore = await fixture.GetBpmnStateAsync();
        Assert.Single(stateBefore.CompensationRuns);
        Assert.Equal(BpmnCompensableStatus.Claimed, Assert.Single(stateBefore.Compensables).Status);

        var killBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-kill");
        await fixture.ResumeAsync(killBookmark, JsonSerializer.SerializeToElement(new EventReceived("kill")));

        // Terminate is logical-only: the run record and its Claimed compensable are retained as-is (no cascade).
        var stateAfter = await fixture.GetBpmnStateAsync();
        Assert.Single(stateAfter.CompensationRuns);
        Assert.Equal(BpmnCompensableStatus.Claimed, Assert.Single(stateAfter.Compensables).Status);
    }

    [Fact]
    public async Task IdenticalRuns_ProduceIdenticalCompensationAndTokenIds()
    {
        var first = await RunReplayAsync();
        var second = await RunReplayAsync();

        Assert.Equal(
            first.Compensables.Select(compensable => $"{compensable.CompensableId}:{compensable.HostElementId}:{compensable.Status}").OrderBy(value => value, StringComparer.Ordinal),
            second.Compensables.Select(compensable => $"{compensable.CompensableId}:{compensable.HostElementId}:{compensable.Status}").OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            first.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}").OrderBy(value => value, StringComparer.Ordinal),
            second.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}").OrderBy(value => value, StringComparer.Ordinal));
    }

    private static async Task<BpmnExecutionState> RunReplayAsync()
    {
        await using var fixture = await CreateFixtureAsync(idCount: 30);
        await fixture.RunAsync(NewTwoHostExecutable(fixture, BpmnRuntimeFixture.CompensateThrow("throw")));
        return await fixture.GetBpmnStateAsync();
    }

    /// <summary>
    /// start → hostA → hostB → thrower → (end, unless the thrower is itself an end event). Both hosts carry a
    /// compensation boundary with a synchronous probe handler.
    /// </summary>
    private static WorkflowExecutable NewTwoHostExecutable(
        BpmnRuntimeFixture fixture, BpmnElement thrower, bool routeThrowToEnd = true)
    {
        var children = new List<ExecutableNode>
        {
            fixture.NewProbeNode("node-hostA"),
            fixture.NewProbeNode("node-hostB"),
            fixture.NewProbeNode("node-hA"),
            fixture.NewProbeNode("node-hB")
        };

        var elements = new List<BpmnElement>
        {
            BpmnRuntimeFixture.StartEvent(),
            BpmnRuntimeFixture.Task("hostA", childNodeId: "node-hostA"),
            BpmnRuntimeFixture.Task("hostB", childNodeId: "node-hostB"),
            BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-hA"),
            BpmnRuntimeFixture.CompensationHandler("handlerB", childNodeId: "node-hB"),
            BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA"),
            BpmnRuntimeFixture.CompensationBoundary("cb-b", "hostB", "handlerB"),
            thrower
        };

        var flows = new List<BpmnSequenceFlow>
        {
            BpmnRuntimeFixture.Flow("flow-1", "start", "hostA"),
            BpmnRuntimeFixture.Flow("flow-2", "hostA", "hostB"),
            BpmnRuntimeFixture.Flow("flow-3", "hostB", thrower.ElementId)
        };
        if (routeThrowToEnd)
        {
            elements.Add(BpmnRuntimeFixture.EndEvent());
            flows.Add(BpmnRuntimeFixture.Flow("flow-4", thrower.ElementId, "end"));
        }

        return fixture.NewExecutable(children, elements, flows);
    }

    private static ExecutableNode NewDecisionNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(BpmnLoopDecisionActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "clr" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

    private static ExecutableNode NewDelayNode(string nodeId) =>
        NewClrNode(nodeId, typeof(Delay), new Dictionary<string, object?> { [nameof(Delay.Duration)] = TimeSpan.FromMinutes(5) });

    private static ExecutableNode NewEventWaitNode(string nodeId, string eventName) =>
        NewClrNode(nodeId, typeof(Elsa.Activities.Primitives.Activities.Event), new Dictionary<string, object?>
        {
            [nameof(Elsa.Activities.Primitives.Activities.Event.EventName)] = eventName,
            [nameof(Elsa.Activities.Primitives.Activities.Event.CanStartWorkflow)] = false
        });

    private static ExecutableNode NewClrNode(string nodeId, Type activityType, IReadOnlyDictionary<string, object?> literals) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "clr" }),
            inputBindings: literals.ToDictionary(
                literal => literal.Key,
                literal => new RuntimeInputBinding(
                    inputKey: literal.Key,
                    targetType: new ValueTypeDescriptor("String"),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(
                        new ValueTypeDescriptor("String"),
                        JsonSerializer.SerializeToElement(literal.Value),
                        ValueProtectionPolicy.InstanceInline)),
                StringComparer.Ordinal),
            metadata: new Dictionary<string, string>());
}
