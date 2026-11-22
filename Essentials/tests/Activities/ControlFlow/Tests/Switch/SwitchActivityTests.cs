using System.Text.Json;
using Elsa.Activities.ControlFlow.Exceptions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Switch.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Tests;

public sealed class SwitchActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Theory]
    [InlineData(ActivityExecutionStatus.Suspended)]
    [InlineData(ActivityExecutionStatus.Faulted)]
    [InlineData(ActivityExecutionStatus.Cancelled)]
    public async Task MatchAny_waits_for_every_current_selected_body(ActivityExecutionStatus pendingStatus)
    {
        var store = new InMemoryActivityExecutionStateStore();
        var node = MatchAnyNode();
        var context = NewContext(node, null);
        await store.SaveAsync(ChildState("a", "act-a", ActivityExecutionStatus.Completed));
        await store.SaveAsync(ChildState("c", "act-c", pendingStatus));
        await store.SaveAsync(ChildState("c", "foreign-c", ActivityExecutionStatus.Completed) with { ParentActivityExecutionId = "other-switch" });
        await store.SaveAsync(ChildState("b", "unselected-b", ActivityExecutionStatus.Completed));
        var activity = BoundMatchAny(store);
        var first = await activity.OnChildCompletedAsync(new(context, "act-a", "a", [ActivityOutcomes.Done]));
        Assert.True(first.IsDeferred);
        await store.SaveAsync(ChildState("c", "act-c", ActivityExecutionStatus.Completed));
        var last = await BoundMatchAny(store).OnChildCompletedAsync(new(context, "act-c", "c", [ActivityOutcomes.Done]));
        Assert.Equal(new[] { "Approved", "Notify" }, last.OutcomeNames);
    }

    [Fact]
    public async Task MatchAny_uses_committed_Break_even_when_its_callback_is_delayed()
    {
        var store = new InMemoryActivityExecutionStateStore();
        var context = NewContext(MatchAnyNode(), null);
        await store.SaveAsync(ChildState("a", "act-a", ActivityExecutionStatus.Completed, ActivityOutcomes.Break));
        await store.SaveAsync(ChildState("c", "act-c", ActivityExecutionStatus.Completed));
        var result = await BoundMatchAny(store).OnChildCompletedAsync(new(context, "act-c", "c", [ActivityOutcomes.Done]));
        Assert.Equal(new[] { ActivityOutcomes.Break }, result.OutcomeNames);
    }

    [Fact]
    public async Task MatchAny_no_match_rejects_unselected_callback_while_Default_waits()
    {
        var store = new InMemoryActivityExecutionStateStore();
        var context = NewContext(MatchAnyNode(approved: false, notify: false), null);
        await store.SaveAsync(ChildState("a", "act-a", ActivityExecutionStatus.Completed));
        var activity = BoundMatchAny(store, approved: false, notify: false);
        await Assert.ThrowsAsync<ControlFlowExecutionException>(() => activity.OnChildCompletedAsync(new(context, "act-a", "a", [ActivityOutcomes.Done])).AsTask());
    }

    private static ExecutableNode MatchAnyNode(bool approved = true, bool notify = true) =>
        SwitchMatchAnyTests.Executable(SwitchModes.MatchAny, approved, false, notify,
            new Dictionary<string, ExecutableNode> { ["a"] = WorkflowExecutionHarness.NewProbeNode("a"), ["c"] = WorkflowExecutionHarness.NewProbeNode("c") }).RootActivity;

    [Theory]
    [InlineData("superseded")]
    [InlineData("descendant")]
    [InlineData("default")]
    public async Task Non_current_or_non_selected_success_cannot_satisfy_a_selected_body(string form)
    {
        var store = new InMemoryActivityExecutionStateStore();
        var context = NewContext(MatchAnyNode(), null);
        await store.SaveAsync(ChildState("a", "act-a", ActivityExecutionStatus.Completed));
        var other = ChildState(form == "default" ? "fallback" : "c", "other", ActivityExecutionStatus.Completed);
        if (form == "superseded") other = other with { Status = ActivityExecutionStatus.Superseded,
            SupersededByActivityExecutionId = "replacement", SupersededAt = DateTimeOffset.UnixEpoch, Completion = null };
        if (form == "descendant") other = other with { ParentActivityExecutionId = "act-a" };
        await store.SaveAsync(other);
        var result = await BoundMatchAny(store).OnChildCompletedAsync(new(context, "act-a", "a", [ActivityOutcomes.Done]));
        Assert.True(result.IsDeferred);
        // Redelivery before the last success cannot complete it either.
        Assert.True((await BoundMatchAny(store).OnChildCompletedAsync(new(context, "act-a", "a", [ActivityOutcomes.Done]))).IsDeferred);
        await Assert.ThrowsAsync<ControlFlowExecutionException>(() => BoundMatchAny(store)
            .OnChildCompletedAsync(new(context, "unknown-invocation", "a", [ActivityOutcomes.Done])).AsTask());
    }

    private static SwitchActivity BoundMatchAny(IActivityExecutionStateStore store, bool approved = true, bool notify = true)
    {
        var activity = new SwitchActivity(store);
        activity.BindStructureInputs(new Dictionary<string, ValueEnvelope>
        {
            [SwitchActivity.ConditionInputKey("a")] = Bool(approved),
            [SwitchActivity.ConditionInputKey("b")] = Bool(false),
            [SwitchActivity.ConditionInputKey("c")] = Bool(notify)
        });
        return activity;
    }

    private static ValueEnvelope Bool(bool value) => ValueEnvelope.Inline(new("Boolean"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);

    private static ActivityExecutionState ChildState(string nodeId, string executionId, ActivityExecutionStatus status, string outcome = ActivityOutcomes.Done) =>
        NewRunningState() with
        {
            Execution = new(executionId, "wfexec-1", nodeId, nodeId, "test/probe", "1.0.0"),
            ParentActivityExecutionId = "actexec-switch", Status = status,
            Completion = status == ActivityExecutionStatus.Completed
                ? new ActivityCompletion(executionId, "attempt", Bool(true), OutcomeSelection.Named(outcome), DateTimeOffset.UnixEpoch, "fingerprint") : null
        };

    [Fact]
    public async Task Execute_SchedulesMatchingCaseBranch()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a")), ("b", NewNode("node-b"))],
            @default: NewNode("node-default")), value: "b");

        var continuation = await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-b", request.ExecutableNodeId);
        Assert.Equal("actexec-switch", request.SchedulingActivityExecutionId);
        Assert.True(continuation.IsDeferred);
    }

    [Fact]
    public async Task Execute_SchedulesDefaultBranch_WhenNoCaseMatches()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a"))],
            @default: NewNode("node-default")), value: "zzz");

        var continuation = await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-default", request.ExecutableNodeId);
    }

    [Fact]
    public async Task Execute_CompletesWithCaseOutcome_WhenMatchingCaseBranchIsAbsent()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", null)],
            @default: NewNode("node-default")), value: "a");

        var continuation = await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(continuation.IsComplete);
        Assert.Equal(new[] { "a" }, continuation.OutcomeNames);
    }

    [Fact]
    public async Task Execute_CompletesWithDefaultOutcome_WhenNoCaseMatchesAndDefaultIsAbsent()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a"))],
            @default: null), value: "zzz");

        var continuation = await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(continuation.IsComplete);
        Assert.Equal(new[] { ActivityOutcomes.Default }, continuation.OutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithCaseOutcome_AfterCaseBranch()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a")), ("b", NewNode("node-b"))],
            @default: NewNode("node-default")), value: "b");

        var continuation = await new SwitchActivity(new InMemoryActivityExecutionStateStore()).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-b", "node-b", [ActivityOutcomes.Done]));

        Assert.True(continuation.IsComplete);
        Assert.Equal(new[] { "b" }, continuation.OutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithDefaultOutcome_AfterDefaultBranch()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a"))],
            @default: NewNode("node-default")), value: "zzz");

        var continuation = await new SwitchActivity(new InMemoryActivityExecutionStateStore()).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-default", "node-default", [ActivityOutcomes.Done]));

        Assert.True(continuation.IsComplete);
        Assert.Equal(new[] { ActivityOutcomes.Default }, continuation.OutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_SelectedCaseBreaks_CompletesWithBreak_InsteadOfCaseOutcome()
    {
        // The selected case branch completes with Break: Switch completes with Break instead of the case's
        // match outcome so it bubbles to the enclosing loop (#299).
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a")), ("b", NewNode("node-b"))],
            @default: NewNode("node-default")), value: "b");

        var continuation = await new SwitchActivity(new InMemoryActivityExecutionStateStore()).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-b", "node-b", [ActivityOutcomes.Break]));

        Assert.True(continuation.IsComplete);
        Assert.Equal(new[] { ActivityOutcomes.Break }, continuation.OutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenCompletedChildIsNotABranch()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a"))],
            @default: NewNode("node-default")), value: "a");

        await Assert.ThrowsAsync<ControlFlowExecutionException>(() => new SwitchActivity(new InMemoryActivityExecutionStateStore())
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-x", "node-x", [ActivityOutcomes.Done]))
            .AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenSlotChildDoesNotMatchStructure()
    {
        // Slot carries a child the structure does not declare as the case branch.
        var node = NewNode(
            "node-switch",
            activityType: "switch",
            childSlots:
            [
                new ExecutableChildSlot(SwitchActivity.CaseSlotName("a"), [NewNode("slot-child")])
            ],
            structure: NewSwitchStructure(cases: [("a", "declared-but-missing")], @default: null));
        var context = NewContext(node, value: "a");

        await Assert.ThrowsAsync<ControlFlowExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenStructureContainsDuplicateCase()
    {
        var node = NewNode(
            "node-switch",
            activityType: "switch",
            childSlots: [new ExecutableChildSlot(SwitchActivity.CaseSlotName("a"), [NewNode("node-a")])],
            structure: NewSwitchStructure(cases: [("a", "node-a"), ("a", "node-a")], @default: null));
        var context = NewContext(node, value: "a");

        await Assert.ThrowsAsync<ControlFlowExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenTwoCasesShareTheSameBranchNodeId()
    {
        // Distinct case keys both reference branch node 'shared': the navigator must reject this with a
        // ControlFlowExecutionException rather than a raw ArgumentException from its node-id lookup.
        var node = NewNode(
            "node-switch",
            activityType: "switch",
            childSlots:
            [
                new ExecutableChildSlot(SwitchActivity.CaseSlotName("a"), [NewNode("shared")]),
                new ExecutableChildSlot(SwitchActivity.CaseSlotName("b"), [NewNode("shared")])
            ],
            structure: NewSwitchStructure(cases: [("a", "shared"), ("b", "shared")], @default: null));
        var context = NewContext(node, value: "a");

        await Assert.ThrowsAsync<ControlFlowExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private static ValueTask<RuntimeStructuralContinuation> ExecuteAsync(SimpleActivityExecutionContext context) =>
        ((IRuntimeStructuralActivity)context.Activity).ExecuteStructureAsync(context);

    private SimpleActivityExecutionContext NewContext(ExecutableNode executableNode, string? value)
    {
        var activity = new SwitchActivity(new InMemoryActivityExecutionStateStore()) { Value = value };
        var context = new SimpleActivityExecutionContext(
            activity,
            CancellationToken.None,
            "wfexec-1",
            NewIdentity(),
            NewWorkItem(),
            executableNode,
            NewRunningState());
        return context;
    }

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution("actexec-switch", "wfexec-1", "node-switch", "authored-switch", "switch", "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static RuntimeSchedulerWorkItem NewWorkItem() =>
        new(
            workItemId: "work-switch",
            workflowExecutionId: "wfexec-1",
            commandId: "command-switch",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:switch",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static ExecutableNode NewSwitchNode((string Match, ExecutableNode? Branch)[] cases, ExecutableNode? @default)
    {
        var childSlots = new List<ExecutableChildSlot>();
        foreach (var (match, branch) in cases)
            if (branch is not null)
                childSlots.Add(new ExecutableChildSlot(SwitchActivity.CaseSlotName(match), [branch]));
        if (@default is not null)
            childSlots.Add(new ExecutableChildSlot(SwitchActivity.DefaultSlotName, [@default]));

        return NewNode(
            "node-switch",
            activityType: "switch",
            childSlots: childSlots,
            structure: NewSwitchStructure(
                cases.Select(@case => (@case.Match, @case.Branch?.ExecutableNodeId)).ToArray(),
                @default?.ExecutableNodeId));
    }

    private static ExecutableNode NewNode(
        string nodeId,
        string activityType = "test/probe",
        IReadOnlyCollection<ExecutableChildSlot>? childSlots = null,
        ExecutableActivityStructure? structure = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: structure);

    private static ExecutableActivityStructure NewSwitchStructure((string Match, string? Activity)[] cases, string? @default) =>
        new(
            SwitchActivity.StructureKind,
            SwitchActivity.LegacyStructureSchemaVersion,
            JsonSerializer.SerializeToElement(new
            {
                cases = cases.Select(@case => new { match = @case.Match, activity = @case.Activity }).ToArray(),
                @default
            }));

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

}
