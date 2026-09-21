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
/// End-to-end coverage for spec 119 event-based gateways: a token forks into a race of intermediate catch
/// events; the first to fire wins and routes, and every losing sibling's token AND its armed child subtree
/// are cancelled (the first BPMN consumer of the spec 112 seam-A subtree cancellation).
/// </summary>
public sealed class BpmnEventBasedGatewayTests
{
    private const string DelayResumeTargetId = "resume-target:durable-timer";

    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(params string[] activityExecutionIds) =>
        BpmnRuntimeFixture.CreateAsync(activityExecutionIds, services =>
            new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));

    [Fact]
    public async Task FirstCatchWins_CancelsLosingSiblingTokenAndSubtree_AndCleansItsBookmark()
    {
        // The winner routes onward to a trailing catch so the process stays Running after the race resolves —
        // a completed BPMN process drops its private state, so the resolved token state is only observable while
        // it still runs. The loser subtree teardown itself is proved via the durable runtime child state.
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-wait", "actexec-delay", "actexec-final");
        var executable = fixture.NewExecutable(
            children:
            [
                NewEventWaitNode("node-wait", "order-shipped"),
                NewDelayNode("node-delay", TimeSpan.FromMinutes(5)),
                NewEventWaitNode("node-final", "all-done")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EventBasedGateway("gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-msg", BpmnEventDefinitionTypes.Message, childNodeId: "node-wait"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-timer", BpmnEventDefinitionTypes.Timer, childNodeId: "node-delay"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-final", BpmnEventDefinitionTypes.Message, childNodeId: "node-final"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "catch-msg"),
                BpmnRuntimeFixture.Flow("flow-3", "gw", "catch-timer"),
                BpmnRuntimeFixture.Flow("flow-4", "catch-msg", "catch-final"),
                BpmnRuntimeFixture.Flow("flow-5", "catch-timer", "end"),
                BpmnRuntimeFixture.Flow("flow-6", "catch-final", "end")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-wait", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-delay", DelayResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-final", EventActivity.ResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);

        // Both catch events arm concurrently and one race is recorded.
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        Assert.Equal(2, (await fixture.BookmarksAsync()).Count);
        var messageBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.StimulusHash == EventStimulus.Hash("order-shipped"));
        var armedRace = Assert.Single((await fixture.GetBpmnStateAsync()).Races);
        Assert.False(armedRace.Resolved);
        Assert.Equal(2, armedRace.MemberTokenIds.Count);

        // The message stimulus fires first: it wins, routes onward, and the timer sibling loses.
        var resumed = await fixture.ResumeAsync(messageBookmark, JsonSerializer.SerializeToElement(new EventReceived("order-shipped")));

        resumed.AssertCompleted("node-wait");
        Assert.Equal(WorkflowExecutionStatus.Running, resumed.WorkflowState?.Status);

        // The losing timer catch's token ended and the race is resolved (observed on the still-running state).
        var resolvedState = await fixture.GetBpmnStateAsync();
        Assert.Contains(resolvedState.Tokens, token => token.AtElementId == "catch-timer" && token.Status == BpmnTokenStatus.Canceled);
        Assert.True(Assert.Single(resolvedState.Races).Resolved);

        // The losing Delay child subtree was torn down through seam A and its durable timer bookmark cleaned.
        var loserChild = resumed.State("node-delay");
        Assert.Equal(ActivityExecutionStatus.Cancelled, loserChild.Status);
        Assert.Equal("ParentCancelled", loserChild.SubStatus);
        Assert.Equal(BpmnExecutionEngine.EventGatewayRaceCancellationReason, loserChild.Metadata[RuntimeMetadataKeys.ScopeCancellationReason]);
        var remainingBookmark = Assert.Single(await fixture.BookmarksAsync());
        Assert.Equal(EventStimulus.Hash("all-done"), remainingBookmark.StimulusHash);
    }

    [Fact]
    public async Task ThreeMemberRace_CancelsBothLosers()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-a", "actexec-b", "actexec-c");
        var executable = fixture.NewExecutable(
            children:
            [
                NewEventWaitNode("node-a", "evt-a"),
                NewEventWaitNode("node-b", "evt-b"),
                NewDelayNode("node-c", TimeSpan.FromMinutes(10))
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EventBasedGateway("gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-a"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-b", BpmnEventDefinitionTypes.Signal, childNodeId: "node-b"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-c", BpmnEventDefinitionTypes.Timer, childNodeId: "node-c"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "catch-a"),
                BpmnRuntimeFixture.Flow("flow-3", "gw", "catch-b"),
                BpmnRuntimeFixture.Flow("flow-4", "gw", "catch-c"),
                BpmnRuntimeFixture.Flow("flow-5", "catch-a", "end"),
                BpmnRuntimeFixture.Flow("flow-6", "catch-b", "end"),
                BpmnRuntimeFixture.Flow("flow-7", "catch-c", "end")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-a", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-b", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-c", DelayResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);
        Assert.Equal(3, (await fixture.BookmarksAsync()).Count);
        var winnerBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.StimulusHash == EventStimulus.Hash("evt-a"));

        var resumed = await fixture.ResumeAsync(winnerBookmark, JsonSerializer.SerializeToElement(new EventReceived("evt-a")));

        // The winner runs to completion; both losing siblings' children are torn down and their bookmarks cleaned.
        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        resumed.AssertWorkflowCompleted();
        foreach (var loser in new[] { "node-b", "node-c" })
        {
            var loserChild = resumed.State(loser);
            Assert.Equal(ActivityExecutionStatus.Cancelled, loserChild.Status);
            Assert.Equal("ParentCancelled", loserChild.SubStatus);
        }

        Assert.Empty(await fixture.BookmarksAsync());
    }

    [Fact]
    public async Task RacingChildCompletions_InOneDrain_AreAbsorbed_WithoutFault()
    {
        // Both catch children are immediate-completing probes, so they complete in the same drain: the first
        // wins and routes; the second arrives on a canceled token and is absorbed (no fault, one path to end).
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-a", "actexec-b"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EventBasedGateway("gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-a"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-b", BpmnEventDefinitionTypes.Signal, childNodeId: "node-b"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "catch-a"),
                BpmnRuntimeFixture.Flow("flow-3", "gw", "catch-b"),
                BpmnRuntimeFixture.Flow("flow-4", "catch-a", "end"),
                BpmnRuntimeFixture.Flow("flow-5", "catch-b", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        // The first probe to complete wins and routes to end; the second arrives on a canceled token (or is
        // acked as a terminal target) and is absorbed — no fault, one clean path, process completes.
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-a").Status);
        Assert.Empty(await fixture.Provider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
    }

    [Fact]
    public async Task WinnerRoutingFault_DoesNotStageCancellations_AndSurfacesTheRoutingFault()
    {
        // The winning catch's own outbound flow is conditioned on an outcome its child never reports, so the
        // winner routing faults bpmn.flow.none-taken. Per D3 the loser cancellations must NOT be staged in that
        // evaluation — the process faults on the routing fault, not on a seam-112 "cannot also cancel" error.
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-wait", "actexec-delay");
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-wait", "order-shipped"), NewDelayNode("node-delay", TimeSpan.FromMinutes(5))],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EventBasedGateway("gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-msg", BpmnEventDefinitionTypes.Message, childNodeId: "node-wait"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-timer", BpmnEventDefinitionTypes.Timer, childNodeId: "node-delay"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "catch-msg"),
                BpmnRuntimeFixture.Flow("flow-3", "gw", "catch-timer"),
                // The winner's outbound requires an outcome the Event child never reports → none-taken.
                BpmnRuntimeFixture.Flow("flow-4", "catch-msg", "end", conditionOutcome: "NeverMatched"),
                BpmnRuntimeFixture.Flow("flow-5", "catch-timer", "end")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-wait", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-delay", DelayResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);
        var messageBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.StimulusHash == EventStimulus.Hash("order-shipped"));

        var resumed = await fixture.ResumeAsync(messageBookmark, JsonSerializer.SerializeToElement(new EventReceived("order-shipped")));

        var process = resumed.State(BpmnRuntimeFixture.ProcessNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, process.Status);
        var persistedState = JsonSerializer.Deserialize<BpmnExecutionState>(
            process.PrivateState!.Value.InlineValue!.Value.GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Contains(persistedState.Diagnostics, diagnostic =>
            diagnostic.Kind == BpmnDiagnosticKind.Faulted && diagnostic.Message.Contains("no outbound sequence flow matched", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Terminate_WhileRaceLive_CancelsMembersLogically_WithoutSeamATeardown()
    {
        // One parallel branch waits on a "kill" event; the other opens a live race. Resuming "kill" routes to a
        // terminate end event: terminate's CancelLiveWork logically cancels the live race members and ends the
        // process, but stages NO seam-A subtree cancellation (D5). The losing race children therefore stay
        // suspended with their bookmarks intact — the observable proof that no runtime teardown happened.
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-kill", "actexec-a", "actexec-b");
        var executable = fixture.NewExecutable(
            children:
            [
                NewEventWaitNode("node-kill", "kill"),
                NewEventWaitNode("node-a", "evt-a"),
                NewEventWaitNode("node-b", "evt-b")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-kill", BpmnEventDefinitionTypes.Message, childNodeId: "node-kill"),
                BpmnRuntimeFixture.EventBasedGateway("gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-a"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-b", BpmnEventDefinitionTypes.Signal, childNodeId: "node-b"),
                BpmnRuntimeFixture.TerminateEndEvent(),
                BpmnRuntimeFixture.EndEvent("end-a"),
                BpmnRuntimeFixture.EndEvent("end-b")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "fork"),
                BpmnRuntimeFixture.Flow("flow-2", "fork", "catch-kill"),
                BpmnRuntimeFixture.Flow("flow-3", "fork", "gw"),
                BpmnRuntimeFixture.Flow("flow-4", "gw", "catch-a"),
                BpmnRuntimeFixture.Flow("flow-5", "gw", "catch-b"),
                BpmnRuntimeFixture.Flow("flow-6", "catch-kill", "terminate"),
                BpmnRuntimeFixture.Flow("flow-7", "catch-a", "end-a"),
                BpmnRuntimeFixture.Flow("flow-8", "catch-b", "end-b")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-kill", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-a", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-b", EventActivity.ResumeTargetId)
            ]);

        var run = await fixture.RunAsync(executable);
        Assert.Equal(3, (await fixture.BookmarksAsync()).Count);
        var killBookmark = Assert.Single(await fixture.BookmarksAsync(), candidate => candidate.StimulusHash == EventStimulus.Hash("kill"));

        var resumed = await fixture.ResumeAsync(killBookmark, JsonSerializer.SerializeToElement(new EventReceived("kill")));

        resumed.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        Assert.Empty(await fixture.Provider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));

        // Logical-only: the race members' children were NOT terminalized through seam A — they stay suspended
        // and their durable event bookmarks survive (contrast with a first-catch win, which cleans them).
        Assert.NotEqual(ActivityExecutionStatus.Cancelled, resumed.State("node-a").Status);
        Assert.NotEqual(ActivityExecutionStatus.Cancelled, resumed.State("node-b").Status);
        var survivingBookmarks = await fixture.BookmarksAsync();
        Assert.Contains(survivingBookmarks, bookmark => bookmark.StimulusHash == EventStimulus.Hash("evt-a"));
        Assert.Contains(survivingBookmarks, bookmark => bookmark.StimulusHash == EventStimulus.Hash("evt-b"));
    }

    [Fact]
    public async Task IdenticalRuns_ProduceIdenticalTokenAndRaceIds()
    {
        var first = await RunArmedRaceAsync();
        var second = await RunArmedRaceAsync();

        Assert.Equal(
            first.Tokens.Select(token => token.TokenId).OrderBy(id => id, StringComparer.Ordinal),
            second.Tokens.Select(token => token.TokenId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(
            first.Races.Select(race => race.RaceId).OrderBy(id => id, StringComparer.Ordinal),
            second.Races.Select(race => race.RaceId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(
            Assert.Single(first.Races).MemberTokenIds.OrderBy(id => id, StringComparer.Ordinal),
            Assert.Single(second.Races).MemberTokenIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    private static async Task<BpmnExecutionState> RunArmedRaceAsync()
    {
        await using var fixture = await CreateFixtureAsync("actexec-bpmn", "actexec-a", "actexec-b");
        var executable = fixture.NewExecutable(
            children: [NewEventWaitNode("node-a", "evt-a"), NewEventWaitNode("node-b", "evt-b")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EventBasedGateway("gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-a"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-b", BpmnEventDefinitionTypes.Signal, childNodeId: "node-b"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "catch-a"),
                BpmnRuntimeFixture.Flow("flow-3", "gw", "catch-b"),
                BpmnRuntimeFixture.Flow("flow-4", "catch-a", "end"),
                BpmnRuntimeFixture.Flow("flow-5", "catch-b", "end")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-a", EventActivity.ResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-b", EventActivity.ResumeTargetId)
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
