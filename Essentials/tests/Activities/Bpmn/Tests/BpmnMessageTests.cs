using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Scheduling;
using Xunit;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for the spec 135 BPMN message send surface: a message throw event schedules its bound
/// <see cref="PublishEvent"/> send child and routes onward on the child's fire-and-continue completion (the send
/// landing as a durable post-commit intent, not an in-execution route); a message end event consumes its token; a
/// sendTask sends and routes; and a receiveTask suspends on the shipped <c>Event</c> bookmark and resumes on delivery.
/// The BPMN execution harness composes <c>ActivitiesPrimitivesFeature</c>, so the publish staging buffer + handler are
/// already wired; the durable-first pin is the committed <c>PublishStimulusIntentKind</c> intent (the harness commits
/// post-commit intents without delivering them, mirroring the DispatchWorkflow BPMN coverage).
/// </summary>
public sealed class BpmnMessageTests
{
    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(params string[] activityExecutionIds) =>
        BpmnRuntimeFixture.CreateAsync(activityExecutionIds, services =>
            new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));

    [Fact]
    public async Task MessageThrow_RoutesOnwardImmediately_AndSendLandsPostCommit()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-send", "actexec-after");
        var executable = fixture.NewExecutable(
            children: [BpmnRuntimeFixture.PublishEventSendNode("node-send", "order-shipped"), fixture.NewProbeNode("node-after")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MessageThrow("throw", childNodeId: "node-send", name: "order-shipped"),
                BpmnRuntimeFixture.Task("task-after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "throw"),
                BpmnRuntimeFixture.Flow("flow-2", "throw", "task-after"),
                BpmnRuntimeFixture.Flow("flow-3", "task-after", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        // The throw's bound send completes fire-and-continue, so the token routes onward to the downstream task.
        run.AssertRan("node-after");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();

        // Durable-first: the send is a committed post-commit intent carrying the shared Event stimulus, not an
        // in-execution route.
        var intent = Assert.Single(fixture.PublishStimulusIntents());
        var payload = intent.Payload!.Value.Deserialize<PublishStimulusIntentPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(EventStimulus.Hash("order-shipped"), payload.StimulusHash);
    }

    [Fact]
    public async Task MessageEnd_ConsumesToken_AndSendLandsPostCommit()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-send");
        var executable = fixture.NewExecutable(
            children: [BpmnRuntimeFixture.PublishEventSendNode("node-send", "process-done")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MessageEnd("end", childNodeId: "node-send", name: "process-done")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        var intent = Assert.Single(fixture.PublishStimulusIntents());
        var payload = intent.Payload!.Value.Deserialize<PublishStimulusIntentPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(EventStimulus.Hash("process-done"), payload.StimulusHash);
    }

    [Fact]
    public async Task SendTask_SendsAndRoutesOnward()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-send", "actexec-after");
        var executable = fixture.NewExecutable(
            children: [BpmnRuntimeFixture.PublishEventSendNode("node-send", "notify"), fixture.NewProbeNode("node-after")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.SendTask("send", childNodeId: "node-send"),
                BpmnRuntimeFixture.Task("task-after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "send"),
                BpmnRuntimeFixture.Flow("flow-2", "send", "task-after"),
                BpmnRuntimeFixture.Flow("flow-3", "task-after", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-after");
        run.AssertWorkflowCompleted();
        var intent = Assert.Single(fixture.PublishStimulusIntents());
        var payload = intent.Payload!.Value.Deserialize<PublishStimulusIntentPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(EventStimulus.Hash("notify"), payload.StimulusHash);
    }

    [Fact]
    public async Task ReceiveTask_SuspendsOnEventBookmark_ThenResumes()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-wait", "actexec-after");
        var executable = fixture.NewExecutable(
            children: [BpmnRuntimeFixture.EventWaitNode("node-wait", "order-approved"), fixture.NewProbeNode("node-after")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ReceiveTask("receive", childNodeId: "node-wait"),
                BpmnRuntimeFixture.Task("task-after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "receive"),
                BpmnRuntimeFixture.Flow("flow-2", "receive", "task-after"),
                BpmnRuntimeFixture.Flow("flow-3", "task-after", "end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-wait", EventActivity.ResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        run.AssertDidNotRun("node-after");
        var bookmark = Assert.Single(await fixture.BookmarksAsync());
        Assert.Equal(EventStimulus.StimulusType, bookmark.StimulusType);
        Assert.Equal(EventStimulus.Hash("order-approved"), bookmark.StimulusHash);

        var resumed = await fixture.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new EventReceived("order-approved")));

        resumed.AssertCompleted("node-wait");
        resumed.AssertRan("node-after");
        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task MultiInstanceSendTask_SendsPerIteration()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-s0", "actexec-s1", "actexec-after");
        var executable = fixture.NewExecutable(
            children: [BpmnRuntimeFixture.PublishEventSendNode("node-send", "fanout"), fixture.NewProbeNode("node-after")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.MultiInstanceSendTask("send", childNodeId: "node-send", cardinality: 2, isSequential: true),
                BpmnRuntimeFixture.Task("task-after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "send"),
                BpmnRuntimeFixture.Flow("flow-2", "send", "task-after"),
                BpmnRuntimeFixture.Flow("flow-3", "task-after", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-after");
        run.AssertWorkflowCompleted();
        // One durable-first send per multi-instance iteration.
        Assert.Equal(2, fixture.PublishStimulusIntents().Count);
    }
}
