using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for spec 122 cyclic sequence flows. A loop-back sequence flow mints a fresh token
/// iteration key; forward propagation inherits it; parallel/inclusive joins group arrivals by
/// <c>(element, iteration key)</c>. These tests exercise a do-while count loop, a parallel fork/join inside a
/// loop body (the conflation-regression case), a catch event in a loop (re-arming + distinct keys), a
/// multi-instance host in a loop, and a boundary host in a loop.
/// </summary>
public sealed class BpmnCyclicFlowTests
{
    private const string DelayResumeTargetId = "resume-target:durable-timer";

    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(BpmnLoopDecisionCounter counter, int idCount, bool withScheduling = false) =>
        BpmnRuntimeFixture.CreateAsync(Ids(idCount), services =>
        {
            services.AddSingleton(counter);
            if (withScheduling)
                new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);
        });

    private static string[] Ids(int count) =>
        new[] { "actexec-bpmn" }.Concat(Enumerable.Range(0, count).Select(index => $"aei-{index}")).ToArray();

    [Fact]
    public async Task DoWhile_ExclusiveGatewayCountLoop_RunsBodyPerPass_AndCompletes()
    {
        // start → body → gw ; gw --again--> body (loop back) ; gw --exit--> end.
        // The decision loops back 3 times, so the body runs 4 times and the process completes.
        var counter = new BpmnLoopDecisionCounter { LoopBackCount = 3 };
        await using var fixture = await CreateFixtureAsync(counter, idCount: 20);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-body"), NewDecisionNode("node-decision")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("body", childNodeId: "node-body"),
                BpmnRuntimeFixture.ExclusiveGateway("gw", childNodeId: "node-decision"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "body"),
                BpmnRuntimeFixture.Flow("flow-2", "body", "gw"),
                BpmnRuntimeFixture.Flow("again", "gw", "body", conditionOutcome: BpmnLoopDecisionActivity.LoopBack),
                BpmnRuntimeFixture.Flow("exit", "gw", "end", isDefault: true)
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(4, run.States("node-body").Count);
        Assert.Equal(4, run.States("node-decision").Count);
    }

    [Fact]
    public async Task ParallelForkJoinInsideLoop_JoinsOncePerIteration_WithCorrectPairing()
    {
        // start → top → fork ; fork→a, fork→b ; a→join, b→join ; join→gw ;
        // gw --again--> top (loop back to a plain task before the fork) ; gw --exit--> end.
        // The loop-back mints a fresh key each pass, so the parallel join pairs each iteration's own
        // (a,b) arrivals — never a cross-iteration pair — and fires exactly once per iteration.
        var counter = new BpmnLoopDecisionCounter { LoopBackCount = 1 }; // 2 iterations
        await using var fixture = await CreateFixtureAsync(counter, idCount: 30);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-top"),
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                NewDecisionNode("node-decision")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("top", childNodeId: "node-top"),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.Task("a", childNodeId: "node-a"),
                BpmnRuntimeFixture.Task("b", childNodeId: "node-b"),
                BpmnRuntimeFixture.ParallelGateway("join"),
                BpmnRuntimeFixture.ExclusiveGateway("gw", childNodeId: "node-decision"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "top"),
                BpmnRuntimeFixture.Flow("flow-2", "top", "fork"),
                BpmnRuntimeFixture.Flow("flow-3", "fork", "a"),
                BpmnRuntimeFixture.Flow("flow-4", "fork", "b"),
                BpmnRuntimeFixture.Flow("flow-5", "a", "join"),
                BpmnRuntimeFixture.Flow("flow-6", "b", "join"),
                BpmnRuntimeFixture.Flow("flow-7", "join", "gw"),
                BpmnRuntimeFixture.Flow("again", "gw", "top", conditionOutcome: BpmnLoopDecisionActivity.LoopBack),
                BpmnRuntimeFixture.Flow("exit", "gw", "end", isDefault: true)
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        // Two iterations: each branch and the post-join decision run exactly twice; the join fires twice.
        Assert.Equal(2, run.States("node-a").Count);
        Assert.Equal(2, run.States("node-b").Count);
        Assert.Equal(2, run.States("node-decision").Count);

        var state = await fixture.GetBpmnStateAsync();
        Assert.Equal(2, state.Diagnostics.Count(diagnostic => diagnostic.Kind == BpmnDiagnosticKind.Joined && diagnostic.ElementId == "join"));
    }

    [Fact]
    public async Task CatchEventInsideLoop_ReArmsPerIteration_WithDistinctIterationKeys()
    {
        // start → catch(Event "tick") → gw ; gw --again--> catch (loop back) ; gw --exit--> end.
        // Each pass re-arms the catch on its own iteration key; we observe the live catch token's key.
        var counter = new BpmnLoopDecisionCounter { LoopBackCount = 2 }; // catch armed 3 times (keys x3)
        await using var fixture = await CreateFixtureAsync(counter, idCount: 20, withScheduling: true);
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-wait", "tick"), NewDecisionNode("node-decision")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch", BpmnEventDefinitionTypes.Message, childNodeId: "node-wait"),
                BpmnRuntimeFixture.ExclusiveGateway("gw", childNodeId: "node-decision"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "catch"),
                BpmnRuntimeFixture.Flow("flow-2", "catch", "gw"),
                BpmnRuntimeFixture.Flow("again", "gw", "catch", conditionOutcome: BpmnLoopDecisionActivity.LoopBack),
                BpmnRuntimeFixture.Flow("exit", "gw", "end", isDefault: true)
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-wait", EventActivity.ResumeTargetId)]);

        var observedKeys = new List<string?>();
        var run = await fixture.RunAsync(executable);

        for (var pass = 0; pass < 3; pass++)
        {
            var catchToken = Assert.Single((await fixture.GetBpmnStateAsync()).Tokens,
                token => token.AtElementId == "catch" && token.Status == BpmnTokenStatus.AwaitingChild);
            observedKeys.Add(catchToken.IterationKey);

            var bookmark = Assert.Single(await fixture.BookmarksAsync());
            run = await fixture.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new EventReceived("tick")));
        }

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Empty(await fixture.BookmarksAsync());
        // First pass is the implicit iteration (null); each later pass carries a distinct fresh key.
        Assert.Null(observedKeys[0]);
        Assert.Equal(3, observedKeys.Distinct().Count());
    }

    [Fact]
    public async Task MultiInstanceHostInsideLoop_EachPassRunsItsOwnInstances()
    {
        // start → mi(cardinality 2, sequential) → gw ; gw --again--> mi ; gw --exit--> end.
        // Each pass starts an independent multi-instance loop (coordinator keyed by token id), so the bound
        // child runs 2× per pass. Two passes → 4 body runs.
        var counter = new BpmnLoopDecisionCounter { LoopBackCount = 1 }; // 2 passes
        await using var fixture = await CreateFixtureAsync(counter, idCount: 30);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-mi"), NewDecisionNode("node-decision")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceTask("mi", childNodeId: "node-mi", cardinality: 2, isSequential: true),
                BpmnRuntimeFixture.ExclusiveGateway("gw", childNodeId: "node-decision"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "mi"),
                BpmnRuntimeFixture.Flow("flow-2", "mi", "gw"),
                BpmnRuntimeFixture.Flow("again", "gw", "mi", conditionOutcome: BpmnLoopDecisionActivity.LoopBack),
                BpmnRuntimeFixture.Flow("exit", "gw", "end", isDefault: true)
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(4, run.States("node-mi").Count);
        Assert.Equal(2, run.States("node-decision").Count);
    }

    [Fact]
    public async Task BoundaryHostInsideLoop_ListenerArmsAndTearsDownPerPass()
    {
        // start → host (Event "proceed", catch timer boundary) → gw ; gw --again--> host ; gw --exit--> end.
        // Each pass arms a fresh timer listener alongside the suspending host; resuming the host completes it
        // and tears the listener down. Two passes → the listener arms twice and is superseded twice.
        var counter = new BpmnLoopDecisionCounter { LoopBackCount = 1 }; // 2 passes
        await using var fixture = await CreateFixtureAsync(counter, idCount: 30, withScheduling: true);
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-host", "proceed"), NewDelayNode("node-timer"), NewDecisionNode("node-decision")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("host", childNodeId: "node-host"),
                BpmnRuntimeFixture.BoundaryEvent("b-timer", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-timer"),
                BpmnRuntimeFixture.ExclusiveGateway("gw", childNodeId: "node-decision"),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-boundary")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
                BpmnRuntimeFixture.Flow("flow-2", "host", "gw"),
                BpmnRuntimeFixture.Flow("again", "gw", "host", conditionOutcome: BpmnLoopDecisionActivity.LoopBack),
                BpmnRuntimeFixture.Flow("exit", "gw", "end-host", isDefault: true),
                BpmnRuntimeFixture.Flow("flow-boundary", "b-timer", "end-boundary")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-host", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-timer", DelayResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);

        // Each pass: the host waits and its timer listener is armed alongside it (two bookmarks).
        for (var pass = 0; pass < 2; pass++)
        {
            var bookmarks = await fixture.BookmarksAsync();
            Assert.Equal(2, bookmarks.Count);
            var hostBookmark = Assert.Single(bookmarks, candidate => candidate.ExecutableNodeId == "node-host");
            run = await fixture.ResumeAsync(hostBookmark, JsonSerializer.SerializeToElement(new EventReceived("proceed")));
        }

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Empty(await fixture.BookmarksAsync());

        var state = await fixture.GetBpmnStateAsync();
        Assert.Equal(2, state.Diagnostics.Count(diagnostic =>
            diagnostic.Kind == BpmnDiagnosticKind.Scheduled && diagnostic.ElementId == "b-timer"));
        Assert.Equal(2, state.Diagnostics.Count(diagnostic =>
            diagnostic.Kind == BpmnDiagnosticKind.Canceled && diagnostic.ElementId == "b-timer"));
    }

    [Fact]
    public async Task IdenticalRuns_ProduceIdenticalIterationKeys()
    {
        var first = await RunCatchLoopAndCollectKeysAsync();
        var second = await RunCatchLoopAndCollectKeysAsync();

        Assert.Equal(first, second);
    }

    private static async Task<IReadOnlyList<string?>> RunCatchLoopAndCollectKeysAsync()
    {
        var counter = new BpmnLoopDecisionCounter { LoopBackCount = 2 };
        await using var fixture = await CreateFixtureAsync(counter, idCount: 20, withScheduling: true);
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-wait", "tick"), NewDecisionNode("node-decision")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch", BpmnEventDefinitionTypes.Message, childNodeId: "node-wait"),
                BpmnRuntimeFixture.ExclusiveGateway("gw", childNodeId: "node-decision"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "catch"),
                BpmnRuntimeFixture.Flow("flow-2", "catch", "gw"),
                BpmnRuntimeFixture.Flow("again", "gw", "catch", conditionOutcome: BpmnLoopDecisionActivity.LoopBack),
                BpmnRuntimeFixture.Flow("exit", "gw", "end", isDefault: true)
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-wait", EventActivity.ResumeTargetId)]);

        var keys = new List<string?>();
        await fixture.RunAsync(executable);
        for (var pass = 0; pass < 3; pass++)
        {
            var catchToken = Assert.Single((await fixture.GetBpmnStateAsync()).Tokens,
                token => token.AtElementId == "catch" && token.Status == BpmnTokenStatus.AwaitingChild);
            keys.Add(catchToken.IterationKey);
            var bookmark = Assert.Single(await fixture.BookmarksAsync());
            await fixture.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new EventReceived("tick")));
        }

        return keys;
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
        NewClrNode(nodeId, typeof(EventActivity), new Dictionary<string, object?>
        {
            [nameof(EventActivity.EventName)] = eventName,
            [nameof(EventActivity.CanStartWorkflow)] = false
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

/// <summary>
/// A test decision activity for bounded loops (spec 122): it emits <see cref="LoopBack"/> for the first
/// <see cref="BpmnLoopDecisionCounter.LoopBackCount"/> invocations, then <see cref="Exit"/>. The count is held
/// on an injected singleton keyed by invocation id, so it is idempotent under replay and deterministic across
/// runs (mirroring the Flowchart #382 stateful loop-once policy).
/// </summary>
[ActivityOutcome(LoopBack)]
[ActivityOutcome(Exit)]
public sealed class BpmnLoopDecisionActivity(BpmnLoopDecisionCounter counter) : Activity<ActivityUnit>
{
    public const string LoopBack = "again";
    public const string Exit = "exit";

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, counter.Decide(context.InvocationId)));
}

/// <summary>Shared, invocation-idempotent loop counter for <see cref="BpmnLoopDecisionActivity"/>.</summary>
public sealed class BpmnLoopDecisionCounter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _iterationByInvocation = new(StringComparer.Ordinal);
    private int _next;

    public int LoopBackCount { get; init; }

    public string Decide(string invocationId)
    {
        int iteration;
        lock (_gate)
        {
            if (!_iterationByInvocation.TryGetValue(invocationId, out iteration))
            {
                iteration = _next++;
                _iterationByInvocation[invocationId] = iteration;
            }
        }

        return iteration < LoopBackCount ? BpmnLoopDecisionActivity.LoopBack : BpmnLoopDecisionActivity.Exit;
    }
}
