using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for spec 120 boundary events: a timer/message/signal boundary arms a suspending
/// listener child alongside the host's bound child (host-completes-first tears the listener down, an
/// interrupting listener tears the host down, a non-interrupting listener runs alongside it), and an error
/// boundary absorbs the host's child fault through seam B instead of faulting the composite.
/// </summary>
public sealed class BpmnBoundaryEventTests
{
    private const string DelayResumeTargetId = "resume-target:durable-timer";

    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(params string[] activityExecutionIds) =>
        BpmnRuntimeFixture.CreateAsync(activityExecutionIds, services =>
            new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));

    [Fact]
    public async Task InterruptingTimerBoundary_FiresFirst_TearsDownHostChild_AndRoutesBoundaryPath()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-1", "actexec-2", "actexec-3");
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-host", "proceed"), NewDelayNode("node-timer", TimeSpan.FromMinutes(5))],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("host", childNodeId: "node-host"),
                BpmnRuntimeFixture.BoundaryEvent("b-timer", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-timer"),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-boundary")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
                BpmnRuntimeFixture.Flow("flow-2", "host", "end-host"),
                BpmnRuntimeFixture.Flow("flow-3", "b-timer", "end-boundary")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-host", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-timer", DelayResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);

        // The host waits on "proceed" while the timer boundary arms its Delay listener alongside it.
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        Assert.Equal(2, (await fixture.BookmarksAsync()).Count);
        var timerBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-timer");

        // The timer fires first: the interrupting boundary tears the host's Event child down and routes.
        var resumed = await fixture.ResumeAsync(timerBookmark, JsonSerializer.SerializeToElement(new DurableTimerElapsed(timerBookmark.StimulusHash)));

        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
        var hostChild = resumed.State("node-host");
        Assert.Equal(ActivityExecutionStatus.Cancelled, hostChild.Status);
        Assert.Equal("ParentCancelled", hostChild.SubStatus);
        Assert.Equal(BpmnExecutionEngine.BoundaryHostInterruptedReason, hostChild.Metadata[RuntimeMetadataKeys.ScopeCancellationReason]);
        Assert.Empty(await fixture.BookmarksAsync());
    }

    [Fact]
    public async Task HostCompletesFirst_TearsDownListener_AndRoutesHostPath()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-1", "actexec-2", "actexec-3");
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-host", "proceed"), NewDelayNode("node-timer", TimeSpan.FromMinutes(5))],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("host", childNodeId: "node-host"),
                BpmnRuntimeFixture.BoundaryEvent("b-timer", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-timer"),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-boundary")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
                BpmnRuntimeFixture.Flow("flow-2", "host", "end-host"),
                BpmnRuntimeFixture.Flow("flow-3", "b-timer", "end-boundary")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-host", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-timer", DelayResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);
        var hostBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-host");

        // The host "proceed" completes first: it routes onward and its still-armed timer listener is torn down.
        var resumed = await fixture.ResumeAsync(hostBookmark, JsonSerializer.SerializeToElement(new EventReceived("proceed")));

        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
        var listenerChild = resumed.State("node-timer");
        Assert.Equal(ActivityExecutionStatus.Cancelled, listenerChild.Status);
        Assert.Equal("ParentCancelled", listenerChild.SubStatus);
        Assert.Equal(BpmnExecutionEngine.BoundarySupersededByHostCompletionReason, listenerChild.Metadata[RuntimeMetadataKeys.ScopeCancellationReason]);
        Assert.Empty(await fixture.BookmarksAsync());
    }

    [Fact]
    public async Task NonInterruptingMessageBoundary_Fires_BoundaryRuns_AndHostStillCompletes()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-1", "actexec-2", "actexec-3");
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-host", "proceed"), NewEventWaitNode("node-ping", "ping")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("host", childNodeId: "node-host"),
                BpmnRuntimeFixture.BoundaryEvent("b-msg", "host", BpmnEventDefinitionTypes.Message, childNodeId: "node-ping", cancelActivity: false),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-boundary")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
                BpmnRuntimeFixture.Flow("flow-2", "host", "end-host"),
                BpmnRuntimeFixture.Flow("flow-3", "b-msg", "end-boundary")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-host", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-ping", EventActivity.ResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);
        var pingBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-ping");

        // The non-interrupting boundary fires: its path runs, but the host keeps waiting (process still Running).
        var afterPing = await fixture.ResumeAsync(pingBookmark, JsonSerializer.SerializeToElement(new EventReceived("ping")));
        Assert.Equal(WorkflowExecutionStatus.Running, afterPing.WorkflowState?.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, afterPing.State("node-ping").Status);
        Assert.NotEqual(ActivityExecutionStatus.Cancelled, afterPing.State("node-host").Status);

        // The host then completes normally; both the boundary path and the host path reached their ends.
        var hostBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-host");
        var afterProceed = await fixture.ResumeAsync(hostBookmark, JsonSerializer.SerializeToElement(new EventReceived("proceed")));
        afterProceed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        afterProceed.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Completed, afterProceed.State("node-host").Status);
    }

    [Fact]
    public async Task ErrorBoundary_AbsorbsHostChildFault_ResolvesIncident_AndRoutesErrorPath()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-fault"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewFaultingNode("node-fault")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("host", childNodeId: "node-fault"),
                BpmnRuntimeFixture.BoundaryEvent("b-error", "host", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-error")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
                BpmnRuntimeFixture.Flow("flow-2", "host", "end-host"),
                BpmnRuntimeFixture.Flow("flow-3", "b-error", "end-error")
            ]);

        var run = await fixture.RunAsync(executable);

        // The host's child faults; the error boundary absorbs it and routes the error path to completion.
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-fault").Status);

        var incident = Assert.Single(await fixture.Provider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(IncidentResolutionAction.Continue, incident.ResolutionAction);
        Assert.NotNull(incident.ResolvedAt);
        Assert.Equal("actexec-bpmn", incident.Metadata[RuntimeMetadataKeys.FaultAbsorbedBy]);
        Assert.Equal(BpmnExecutionEngine.ErrorBoundaryAbsorptionReason, incident.Metadata[RuntimeMetadataKeys.FaultAbsorptionReason]);
    }

    [Fact]
    public async Task ErrorBoundary_WithSiblingCatchListener_CancelsListener_OnAbsorb()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-1", "actexec-2", "actexec-3");
        var executable = fixture.NewExecutable(
            children: [fixture.NewFaultingNode("node-fault"), NewDelayNode("node-timer", TimeSpan.FromMinutes(5))],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("host", childNodeId: "node-fault"),
                BpmnRuntimeFixture.BoundaryEvent("b-error", "host", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.BoundaryEvent("b-timer", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-timer"),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-error")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
                BpmnRuntimeFixture.Flow("flow-2", "host", "end-host"),
                BpmnRuntimeFixture.Flow("flow-3", "b-error", "end-error"),
                BpmnRuntimeFixture.Flow("flow-4", "b-timer", "end-host")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-timer", DelayResumeTargetId)]);

        var run = await fixture.RunAsync(executable);

        // The faulting host child absorbs through the error boundary; the sibling timer listener is torn down.
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-fault").Status);
        var listenerChild = run.State("node-timer");
        Assert.Equal(ActivityExecutionStatus.Cancelled, listenerChild.Status);
        Assert.Equal(BpmnExecutionEngine.BoundaryHostInterruptedReason, listenerChild.Metadata[RuntimeMetadataKeys.ScopeCancellationReason]);

        var incident = Assert.Single(await fixture.Provider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Empty(await fixture.BookmarksAsync());
    }

    [Fact]
    public async Task ChildFault_WithoutErrorBoundary_FaultsComposite_Unchanged()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-fault"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewFaultingNode("node-fault")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("host", childNodeId: "node-fault"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
                BpmnRuntimeFixture.Flow("flow-2", "host", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        var process = run.State(BpmnRuntimeFixture.ProcessNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, process.Status);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        var persistedState = JsonSerializer.Deserialize<BpmnExecutionState>(
            process.PrivateState!.Value.InlineValue!.Value.GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Contains(persistedState.Diagnostics, diagnostic =>
            diagnostic.Kind == BpmnDiagnosticKind.Faulted && diagnostic.Message.Contains("faulted child cannot complete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultipleBoundaries_OnOneHost_BehavePerKind()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-1", "actexec-2", "actexec-3", "actexec-4");
        var executable = fixture.NewExecutable(
            children:
            [
                NewEventWaitNode("node-host", "proceed"),
                NewDelayNode("node-timer", TimeSpan.FromMinutes(5)),
                NewEventWaitNode("node-ping", "ping")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("host", childNodeId: "node-host"),
                BpmnRuntimeFixture.BoundaryEvent("b-timer", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-timer"),
                BpmnRuntimeFixture.BoundaryEvent("b-msg", "host", BpmnEventDefinitionTypes.Message, childNodeId: "node-ping", cancelActivity: false),
                BpmnRuntimeFixture.BoundaryEvent("b-error", "host", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-ping"),
                BpmnRuntimeFixture.EndEvent("end-error")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
                BpmnRuntimeFixture.Flow("flow-2", "host", "end-host"),
                BpmnRuntimeFixture.Flow("flow-3", "b-timer", "end-host"),
                BpmnRuntimeFixture.Flow("flow-4", "b-msg", "end-ping"),
                BpmnRuntimeFixture.Flow("flow-5", "b-error", "end-error")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-host", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-timer", DelayResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-ping", EventActivity.ResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);

        // Three bookmarks: host "proceed", timer, non-interrupting "ping" (the error boundary arms nothing).
        Assert.Equal(3, (await fixture.BookmarksAsync()).Count);

        // The non-interrupting message boundary fires first: its path runs, host and timer stay live.
        var pingBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-ping");
        var afterPing = await fixture.ResumeAsync(pingBookmark, JsonSerializer.SerializeToElement(new EventReceived("ping")));
        Assert.Equal(WorkflowExecutionStatus.Running, afterPing.WorkflowState?.Status);
        Assert.NotEqual(ActivityExecutionStatus.Cancelled, afterPing.State("node-host").Status);
        Assert.NotEqual(ActivityExecutionStatus.Cancelled, afterPing.State("node-timer").Status);

        // The host completes next: it routes and tears down the still-armed timer listener.
        var hostBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.ExecutableNodeId == "node-host");
        var afterProceed = await fixture.ResumeAsync(hostBookmark, JsonSerializer.SerializeToElement(new EventReceived("proceed")));
        afterProceed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        afterProceed.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Cancelled, afterProceed.State("node-timer").Status);
        Assert.Empty(await fixture.BookmarksAsync());
    }

    [Fact]
    public async Task IdenticalRuns_ProduceIdenticalTokenIds()
    {
        var first = await RunArmedBoundaryAsync();
        var second = await RunArmedBoundaryAsync();

        Assert.Equal(
            first.Tokens.Select(token => token.TokenId).OrderBy(id => id, StringComparer.Ordinal),
            second.Tokens.Select(token => token.TokenId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(
            first.Tokens.Select(token => $"{token.AtElementId}:{token.ParentTokenId}").OrderBy(value => value, StringComparer.Ordinal),
            second.Tokens.Select(token => $"{token.AtElementId}:{token.ParentTokenId}").OrderBy(value => value, StringComparer.Ordinal));
    }

    private static async Task<BpmnExecutionState> RunArmedBoundaryAsync()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-1", "actexec-2");
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-host", "proceed"), NewDelayNode("node-timer", TimeSpan.FromMinutes(5))],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("host", childNodeId: "node-host"),
                BpmnRuntimeFixture.BoundaryEvent("b-timer", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-timer"),
                BpmnRuntimeFixture.EndEvent("end-host"),
                BpmnRuntimeFixture.EndEvent("end-boundary")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
                BpmnRuntimeFixture.Flow("flow-2", "host", "end-host"),
                BpmnRuntimeFixture.Flow("flow-3", "b-timer", "end-boundary")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-host", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-timer", DelayResumeTargetId)
            ]);

        await fixture.RunAsync(executable);
        return await fixture.GetBpmnStateAsync();
    }

    private static ExecutableNode NewDelayNode(string nodeId, TimeSpan duration) =>
        NewClrNode(nodeId, typeof(Delay), new Dictionary<string, object?> { [nameof(Delay.Duration)] = duration });

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
