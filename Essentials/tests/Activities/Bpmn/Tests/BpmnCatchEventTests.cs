using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Primitives.Activities;
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
/// End-to-end coverage for spec 116 intermediate catch events: a timer catch event waits on a bound
/// <see cref="Delay"/> child (durable timer + bookmark) and message/signal catch events wait on a bound
/// mid-flow <see cref="EventActivity"/> child; resuming the child's bookmark routes the parked token.
/// </summary>
public sealed class BpmnCatchEventTests
{
    private const string DelayResumeTargetId = "resume-target:durable-timer";

    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(params string[] activityExecutionIds) =>
        BpmnRuntimeFixture.CreateAsync(activityExecutionIds, services =>
            new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));

    [Fact]
    public async Task TimerCatchEvent_SuspendsOnDelayChild_ThenRoutesOnTimerResume()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-delay", "actexec-after");
        var executable = fixture.NewExecutable(
            children: [NewDelayNode("node-delay", TimeSpan.FromMinutes(5)), fixture.NewProbeNode("node-after")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-timer", BpmnEventDefinitionTypes.Timer, childNodeId: "node-delay"),
                BpmnRuntimeFixture.Task("task-after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "catch-timer"),
                BpmnRuntimeFixture.Flow("flow-2", "catch-timer", "task-after"),
                BpmnRuntimeFixture.Flow("flow-3", "task-after", "end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-delay", DelayResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        // The process parks the token and stays running while the Delay child holds the durable timer.
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        run.AssertDidNotRun("node-after");
        var state = await fixture.GetBpmnStateAsync();
        Assert.Contains(state.Tokens, token => token.AtElementId == "catch-timer" && token.Status == BpmnTokenStatus.AwaitingChild);

        var bookmark = Assert.Single(await fixture.BookmarksAsync());
        Assert.Equal("DurableTimer", bookmark.StimulusType);
        var timer = await fixture.Provider.GetRequiredService<IDurableTimerStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, bookmark.StimulusHash);
        Assert.NotNull(timer);
        Assert.Equal(TimeSpan.FromMinutes(5), timer!.DueTime - timer.CreatedAt);

        var resumed = await fixture.ResumeAsync(
            bookmark,
            JsonSerializer.SerializeToElement(new DurableTimerElapsed(bookmark.StimulusHash)));

        resumed.AssertCompleted("node-delay");
        resumed.AssertRan("node-after");
        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
    }

    [Theory]
    [InlineData(BpmnEventDefinitionTypes.Message)]
    [InlineData(BpmnEventDefinitionTypes.Signal)]
    public async Task MessageOrSignalCatchEvent_WaitsOnEventChild_ThenRoutesOnStimulusResume(string definitionType)
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-wait", "actexec-after");
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-wait", "order-shipped"), fixture.NewProbeNode("node-after")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-event", definitionType, childNodeId: "node-wait"),
                BpmnRuntimeFixture.Task("task-after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "catch-event"),
                BpmnRuntimeFixture.Flow("flow-2", "catch-event", "task-after"),
                BpmnRuntimeFixture.Flow("flow-3", "task-after", "end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-wait", EventActivity.ResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        // The waiting Event child suspends on the named-event stimulus identity, so raising the event
        // through the existing stimulus dispatch surface resolves to this bookmark.
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        var bookmark = Assert.Single(await fixture.BookmarksAsync());
        Assert.Equal(EventStimulus.StimulusType, bookmark.StimulusType);
        Assert.Equal(EventStimulus.Hash("order-shipped"), bookmark.StimulusHash);

        var resumed = await fixture.ResumeAsync(
            bookmark,
            JsonSerializer.SerializeToElement(new EventReceived("order-shipped")));

        resumed.AssertCompleted("node-wait");
        resumed.AssertRan("node-after");
        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task TwoCatchEvents_OnParallelBranches_ResumeIndependently_AndJoin()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-wait-a", "actexec-wait-b");
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-wait-a", "evt-a"), NewEventWaitNode("node-wait-b", "evt-b")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-wait-a"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-b", BpmnEventDefinitionTypes.Signal, childNodeId: "node-wait-b"),
                BpmnRuntimeFixture.ParallelGateway("join"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "fork"),
                BpmnRuntimeFixture.Flow("flow-2", "fork", "catch-a"),
                BpmnRuntimeFixture.Flow("flow-3", "fork", "catch-b"),
                BpmnRuntimeFixture.Flow("flow-4", "catch-a", "join"),
                BpmnRuntimeFixture.Flow("flow-5", "catch-b", "join"),
                BpmnRuntimeFixture.Flow("flow-6", "join", "end")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-wait-a", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-wait-b", EventActivity.ResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);

        // Both catch events wait concurrently, each on its own node-scoped resume handle (PR #911).
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        var bookmarks = await fixture.BookmarksAsync();
        Assert.Equal(2, bookmarks.Count);
        var bookmarkA = Assert.Single(bookmarks, candidate => candidate.StimulusHash == EventStimulus.Hash("evt-a"));
        var bookmarkB = Assert.Single(bookmarks, candidate => candidate.StimulusHash == EventStimulus.Hash("evt-b"));

        var afterFirst = await fixture.ResumeAsync(bookmarkA, JsonSerializer.SerializeToElement(new EventReceived("evt-a")));

        // One arrival is not enough: the join still waits for the second inbound flow.
        Assert.Equal(WorkflowExecutionStatus.Running, afterFirst.WorkflowState?.Status);
        afterFirst.AssertCompleted("node-wait-a");

        var afterSecond = await fixture.ResumeAsync(bookmarkB, JsonSerializer.SerializeToElement(new EventReceived("evt-b")));

        afterSecond.AssertCompleted("node-wait-b");
        afterSecond.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        afterSecond.AssertWorkflowCompleted();
    }

    /// <summary>A timer catch event's bound child: the existing durable <see cref="Delay"/> (no new wait machinery).</summary>
    private static ExecutableNode NewDelayNode(string nodeId, TimeSpan duration) =>
        NewClrNode(nodeId, typeof(Delay), new Dictionary<string, object?> { [nameof(Delay.Duration)] = duration });

    /// <summary>A message/signal catch event's bound child: a mid-flow <see cref="EventActivity"/> wait.</summary>
    private static ExecutableNode NewEventWaitNode(string nodeId, string eventName) =>
        NewClrNode(nodeId, typeof(EventActivity), new Dictionary<string, object?>
        {
            [nameof(EventActivity.EventName)] = eventName,
            [nameof(EventActivity.CanStartWorkflow)] = false
        });

    /// <summary>
    /// Builds a plain CLR child node; the harness pins the full activity contract from the CLR type at
    /// run time (the production compiler's job), so only the type and literal inputs are declared here.
    /// </summary>
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
