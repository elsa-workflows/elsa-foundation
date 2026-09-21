using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// End-to-end coverage for spec 125 transactions + cancel events: a cancel end event stops other live work,
/// replays the scope's registered compensables newest-first (riding the spec-124 machinery), and completes the
/// process with the distinguishable <c>Cancelled</c> outcome; an empty log completes immediately; the parent maps
/// a transaction child's Cancelled completion to its cancel boundary (or faults when none is attached); a
/// transaction completing Done behaves like a plain subprocess; a handler fault mid-cancel faults the composite;
/// and the id stream is deterministic.
/// </summary>
public sealed class BpmnTransactionTests
{
    private const string DelayResumeTargetId = "resume-target:durable-timer";

    private static ValueTask<BpmnRuntimeFixture> CreateFixtureAsync(int idCount, bool withScheduling = false) =>
        BpmnRuntimeFixture.CreateAsync(Ids(idCount), services =>
        {
            if (withScheduling)
                new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);
        });

    private static string[] Ids(int count) =>
        new[] { "actexec-bpmn" }.Concat(Enumerable.Range(0, count).Select(index => $"aei-{index}")).ToArray();

    [Fact]
    public async Task RootTransaction_CancelEnd_ReplaysCompensablesNewestFirst_CompletesCancelled()
    {
        // A ROOT transaction: start → hostA → hostB → cancel-end. hostA then hostB complete (register compA then
        // compB). The cancel end replays newest-first (handlerB, then handlerA — pinned by suspending Delay handlers
        // resumed one at a time), then completes the process with the Cancelled outcome.
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
                BpmnRuntimeFixture.CancelEnd("cancel-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "hostA"),
                BpmnRuntimeFixture.Flow("flow-2", "hostA", "hostB"),
                BpmnRuntimeFixture.Flow("flow-3", "hostB", "cancel-end")
            ],
            resumeTargets:
            [
                BpmnRuntimeFixture.NodeResumeTarget("node-hA", DelayResumeTargetId),
                BpmnRuntimeFixture.NodeResumeTarget("node-hB", DelayResumeTargetId)
            ],
            isTransaction: true);

        var run = await fixture.RunAsync(executable);

        // While the first handler is suspended, the process is deferred: its durable state shows the cancel verdict
        // and both compensables claimed (the run is alive), coverage the completed-Cancelled tests cannot make.
        var midState = await fixture.GetBpmnStateAsync();
        Assert.True(midState.Cancelling);
        Assert.Single(midState.CompensationRuns);
        Assert.Equal(2, midState.Compensables.Count);
        Assert.All(midState.Compensables, compensable => Assert.Equal(BpmnCompensableStatus.Claimed, compensable.Status));

        // handlerB (most-recently-completed host) replays first, alone.
        var firstBookmark = Assert.Single(await fixture.BookmarksAsync());
        Assert.Equal("node-hB", firstBookmark.ExecutableNodeId);
        run = await fixture.ResumeAsync(firstBookmark, JsonSerializer.SerializeToElement(new DurableTimerElapsed(firstBookmark.StimulusHash)));

        // then handlerA, alone.
        var secondBookmark = Assert.Single(await fixture.BookmarksAsync());
        Assert.Equal("node-hA", secondBookmark.ExecutableNodeId);
        run = await fixture.ResumeAsync(secondBookmark, JsonSerializer.SerializeToElement(new DurableTimerElapsed(secondBookmark.StimulusHash)));

        run.AssertOutcomes(BpmnRuntimeFixture.ProcessNodeId, "Cancelled");
        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-hA"));
        Assert.Single(run.States("node-hB"));
    }

    [Fact]
    public async Task RootTransaction_EmptyLog_CompletesCancelledImmediately()
    {
        // A root transaction with nothing registered: the cancel end completes the process Cancelled with no replay.
        await using var fixture = await CreateFixtureAsync(idCount: 10);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("task-a", childNodeId: "node-a"),
                BpmnRuntimeFixture.CancelEnd("cancel-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "task-a"),
                BpmnRuntimeFixture.Flow("flow-2", "task-a", "cancel-end")
            ],
            isTransaction: true);

        var run = await fixture.RunAsync(executable);

        // The empty-log cancel completes with the Cancelled outcome and runs no compensation handler.
        run.AssertOutcomes(BpmnRuntimeFixture.ProcessNodeId, "Cancelled");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task RootTransaction_CancelEnd_StopsOtherLiveBranch()
    {
        // start → fork ; fork → hostA → cancel-end ; fork → waiter(Event, never resumed). The cancel end stops the
        // still-suspended waiter branch logically and completes the process Cancelled.
        await using var fixture = await CreateFixtureAsync(idCount: 20, withScheduling: true);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-hostA"), fixture.NewProbeNode("node-hA"), NewEventWaitNode("node-wait", "never")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.Task("hostA", childNodeId: "node-hostA"),
                BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-hA"),
                BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA"),
                BpmnRuntimeFixture.Task("waiter", childNodeId: "node-wait"),
                BpmnRuntimeFixture.CancelEnd("cancel-end"),
                BpmnRuntimeFixture.EndEvent("dead-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "fork"),
                BpmnRuntimeFixture.Flow("flow-2", "fork", "hostA"),
                BpmnRuntimeFixture.Flow("flow-3", "hostA", "cancel-end"),
                BpmnRuntimeFixture.Flow("flow-4", "fork", "waiter"),
                BpmnRuntimeFixture.Flow("flow-5", "waiter", "dead-end")
            ],
            resumeTargets: [BpmnRuntimeFixture.NodeResumeTarget("node-wait", Elsa.Activities.Primitives.Activities.Event.ResumeTargetId)],
            isTransaction: true);

        var run = await fixture.RunAsync(executable);

        run.AssertOutcomes(BpmnRuntimeFixture.ProcessNodeId, "Cancelled");
        run.AssertWorkflowCompleted();
        // handlerA compensated hostA; the waiter branch was stopped (its token canceled), so its dead-end never fired.
        Assert.Single(run.States("node-hA"));
    }

    [Fact]
    public async Task ParentBoundary_TransactionCancelled_RoutesCancellationPath()
    {
        // Outer: start → txn → normal-end ; cancel boundary(cb on txn) → cancelled(probe) → cancelled-end.
        // The nested transaction cancels itself (empty log → immediate Cancelled), so the parent fires the cancel
        // boundary and routes the cancellation path (never the normal outbound).
        await using var fixture = await CreateFixtureAsync(idCount: 20);
        var executable = fixture.NewExecutable(
            children: [NewCancellingTransactionNode("node-txn"), fixture.NewProbeNode("node-cancelled")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.TransactionSubProcess("txn", "node-txn"),
                BpmnRuntimeFixture.CancelBoundary("cb", "txn"),
                BpmnRuntimeFixture.Task("cancelled", childNodeId: "node-cancelled"),
                BpmnRuntimeFixture.EndEvent("normal-end"),
                BpmnRuntimeFixture.EndEvent("cancelled-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "txn"),
                BpmnRuntimeFixture.Flow("flow-2", "txn", "normal-end"),
                BpmnRuntimeFixture.Flow("flow-3", "cb", "cancelled"),
                BpmnRuntimeFixture.Flow("flow-4", "cancelled", "cancelled-end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted("node-txn");
        run.AssertWorkflowCompleted();
        // The cancellation path ran (the boundary routed to the cancelled probe).
        Assert.Single(run.States("node-cancelled"));
    }

    [Fact]
    public async Task ParentBoundary_MissingCancelBoundary_Faults()
    {
        // Outer: start → txn → end, with NO cancel boundary. The nested transaction cancels, so the parent faults
        // deterministically (bpmn.transaction.cancelled-unhandled).
        await using var fixture = await CreateFixtureAsync(idCount: 20);
        var executable = fixture.NewExecutable(
            children: [NewCancellingTransactionNode("node-txn")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.TransactionSubProcess("txn", "node-txn"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "txn"),
                BpmnRuntimeFixture.Flow("flow-2", "txn", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(BpmnRuntimeFixture.ProcessNodeId).Status);
        Assert.Equal("bpmn.transaction.cancelled-unhandled", run.State(BpmnRuntimeFixture.ProcessNodeId).Fault?.Code);
    }

    [Fact]
    public async Task DoneTransaction_RoutesNormally_LikePlainSubprocess()
    {
        // A transaction completing Done (a nested none-end process) routes its normal outbound flow, and the cancel
        // boundary stays dormant. Byte-identical to a plain subprocess completion.
        await using var fixture = await CreateFixtureAsync(idCount: 20);
        var executable = fixture.NewExecutable(
            children: [NewDoneTransactionNode("node-txn"), fixture.NewProbeNode("node-after"), fixture.NewProbeNode("node-cancelled")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.TransactionSubProcess("txn", "node-txn"),
                BpmnRuntimeFixture.CancelBoundary("cb", "txn"),
                BpmnRuntimeFixture.Task("after", childNodeId: "node-after"),
                BpmnRuntimeFixture.Task("cancelled", childNodeId: "node-cancelled"),
                BpmnRuntimeFixture.EndEvent("normal-end"),
                BpmnRuntimeFixture.EndEvent("cancelled-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "txn"),
                BpmnRuntimeFixture.Flow("flow-2", "txn", "after"),
                BpmnRuntimeFixture.Flow("flow-3", "after", "normal-end"),
                BpmnRuntimeFixture.Flow("flow-4", "cb", "cancelled"),
                BpmnRuntimeFixture.Flow("flow-5", "cancelled", "cancelled-end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-after"));       // normal path ran
        Assert.Empty(run.States("node-cancelled"));     // cancel boundary stayed dormant
    }

    [Fact]
    public async Task DoneTransaction_WithCompensationBoundary_RegistersCompensable_AndComposesWithSpec124()
    {
        // spec-124 composition: a transaction element carrying its OWN compensation boundary registers a
        // compensable on Done completion (a completed transaction is compensable as a unit). start → txn(Done,
        // compensation boundary cb-txn → handler-txn) → throw(compensate) → end; the throw replays handler-txn.
        await using var fixture = await CreateFixtureAsync(idCount: 20);
        var executable = fixture.NewExecutable(
            children: [NewDoneTransactionNode("node-txn"), fixture.NewProbeNode("node-htxn")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.TransactionSubProcess("txn", "node-txn"),
                BpmnRuntimeFixture.CompensationHandler("handler-txn", childNodeId: "node-htxn"),
                BpmnRuntimeFixture.CompensationBoundary("cb-txn", "txn", "handler-txn"),
                BpmnRuntimeFixture.CompensateThrow("throw"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "txn"),
                BpmnRuntimeFixture.Flow("flow-2", "txn", "throw"),
                BpmnRuntimeFixture.Flow("flow-3", "throw", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
        // The transaction registered on Done and the compensate throw replayed its handler.
        Assert.Single(run.States("node-htxn"));
    }

    [Fact]
    public async Task HandlerFault_MidCancel_FaultsComposite()
    {
        // A root transaction whose compensation handler faults during the cancel replay faults the composite; the
        // process does not complete Cancelled.
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-host", "actexec-handler"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-host"), fixture.NewFaultingNode("node-h")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("hostA", childNodeId: "node-host"),
                BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-h"),
                BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA"),
                BpmnRuntimeFixture.CancelEnd("cancel-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "hostA"),
                BpmnRuntimeFixture.Flow("flow-2", "hostA", "cancel-end")
            ],
            isTransaction: true);

        var run = await fixture.RunAsync(executable);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(BpmnRuntimeFixture.ProcessNodeId).Status);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task IdenticalRuns_ProduceIdenticalCancelIds()
    {
        var first = await RunCancelAsync();
        var second = await RunCancelAsync();

        Assert.Equal(
            first.Compensables.Select(compensable => $"{compensable.CompensableId}:{compensable.HostElementId}:{compensable.Status}").OrderBy(value => value, StringComparer.Ordinal),
            second.Compensables.Select(compensable => $"{compensable.CompensableId}:{compensable.HostElementId}:{compensable.Status}").OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            first.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}").OrderBy(value => value, StringComparer.Ordinal),
            second.Tokens.Select(token => $"{token.TokenId}:{token.AtElementId}:{token.ParentTokenId}").OrderBy(value => value, StringComparer.Ordinal));
    }

    private static async Task<BpmnExecutionState> RunCancelAsync()
    {
        await using var fixture = await CreateFixtureAsync(idCount: 30);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-hostA"), fixture.NewProbeNode("node-hA")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("hostA", childNodeId: "node-hostA"),
                BpmnRuntimeFixture.CompensationHandler("handlerA", childNodeId: "node-hA"),
                BpmnRuntimeFixture.CompensationBoundary("cb-a", "hostA", "handlerA"),
                BpmnRuntimeFixture.CancelEnd("cancel-end")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "hostA"),
                BpmnRuntimeFixture.Flow("flow-2", "hostA", "cancel-end")
            ],
            isTransaction: true);
        await fixture.RunAsync(executable);
        return await fixture.GetBpmnStateAsync();
    }

    /// <summary>A nested transaction <c>BpmnProcess</c> node that cancels itself immediately: inner-start → inner-cancel-end (empty log → Cancelled).</summary>
    private static ExecutableNode NewCancellingTransactionNode(string nodeId) =>
        NewNestedTransactionNode(nodeId,
            elements: [BpmnRuntimeFixture.StartEvent("inner-start"), BpmnRuntimeFixture.CancelEnd("inner-cancel-end")],
            flows: [BpmnRuntimeFixture.Flow("inner-flow-1", "inner-start", "inner-cancel-end")]);

    /// <summary>A nested transaction <c>BpmnProcess</c> node that completes normally (Done): inner-start → inner-end.</summary>
    private static ExecutableNode NewDoneTransactionNode(string nodeId) =>
        NewNestedTransactionNode(nodeId,
            elements: [BpmnRuntimeFixture.StartEvent("inner-start"), BpmnRuntimeFixture.EndEvent("inner-end")],
            flows: [BpmnRuntimeFixture.Flow("inner-flow-1", "inner-start", "inner-end")]);

    private static ExecutableNode NewNestedTransactionNode(string nodeId, IReadOnlyCollection<BpmnElement> elements, IReadOnlyCollection<BpmnSequenceFlow> flows) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "InnerDescriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, [])],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(elements, flows, isTransaction: true))));

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
