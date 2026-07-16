using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Switch.Exceptions;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Tests;

public sealed class SwitchActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_SchedulesMatchingCaseBranch()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a")), ("b", NewNode("node-b"))],
            @default: NewNode("node-default")), value: "b");

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-b", request.ExecutableNodeId);
        Assert.Equal("actexec-switch", request.SchedulingActivityExecutionId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task Execute_SchedulesDefaultBranch_WhenNoCaseMatches()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a"))],
            @default: NewNode("node-default")), value: "zzz");

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-default", request.ExecutableNodeId);
    }

    [Fact]
    public async Task Execute_CompletesWithCaseOutcome_WhenMatchingCaseBranchIsAbsent()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", null)],
            @default: NewNode("node-default")), value: "a");

        await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal(["a"], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task Execute_CompletesWithDefaultOutcome_WhenNoCaseMatchesAndDefaultIsAbsent()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a"))],
            @default: null), value: "zzz");

        await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Default], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithCaseOutcome_AfterCaseBranch()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a")), ("b", NewNode("node-b"))],
            @default: NewNode("node-default")), value: "b");

        await new SwitchActivity().OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-b", "node-b", [ActivityOutcomes.Done]));

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal(["b"], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithDefaultOutcome_AfterDefaultBranch()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a"))],
            @default: NewNode("node-default")), value: "zzz");

        await new SwitchActivity().OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-default", "node-default", [ActivityOutcomes.Done]));

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Default], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_SelectedCaseBreaks_CompletesWithBreak_InsteadOfCaseOutcome()
    {
        // The selected case branch completes with Break: Switch completes with Break instead of the case's
        // match outcome so it bubbles to the enclosing loop (#299).
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a")), ("b", NewNode("node-b"))],
            @default: NewNode("node-default")), value: "b");

        await new SwitchActivity().OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-b", "node-b", [ActivityOutcomes.Break]));

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Break], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenCompletedChildIsNotABranch()
    {
        var context = NewContext(NewSwitchNode(
            cases: [("a", NewNode("node-a"))],
            @default: NewNode("node-default")), value: "a");

        await Assert.ThrowsAsync<SwitchExecutionException>(() => new SwitchActivity()
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

        await Assert.ThrowsAsync<SwitchExecutionException>(() => ExecuteAsync(context).AsTask());
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

        await Assert.ThrowsAsync<SwitchExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenTwoCasesShareTheSameBranchNodeId()
    {
        // Distinct case keys both reference branch node 'shared': the navigator must reject this with a
        // SwitchExecutionException rather than a raw ArgumentException from its node-id lookup.
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

        await Assert.ThrowsAsync<SwitchExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenRuntimeContextIsMissing()
    {
        var context = new NonRuntimeActivityExecutionContext(_serviceProvider, new SwitchActivity());

        await Assert.ThrowsAsync<SwitchExecutionException>(() => ((IActivity)new SwitchActivity()).ExecuteAsync(context).AsTask());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private async ValueTask ExecuteAsync(SimpleActivityExecutionContext context) =>
        await ((IActivity)context.Activity).ExecuteAsync(context);

    private SimpleActivityExecutionContext NewContext(ExecutableNode executableNode, string value)
    {
        var activity = new SwitchActivity { Id = "actexec-switch", NodeId = "node-switch", Value = value };
        var context = new SimpleActivityExecutionContext(
            _serviceProvider,
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
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: structure);

    private static ExecutableActivityStructure NewSwitchStructure((string Match, string? Activity)[] cases, string? @default) =>
        new(
            SwitchActivity.StructureKind,
            SwitchActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new
            {
                cases = cases.Select(@case => new { match = @case.Match, activity = @case.Activity }).ToArray(),
                @default
            }));

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class NonRuntimeActivityExecutionContext(IServiceProvider serviceProvider, IActivity activity) : IActivityExecutionContext
    {
        public IActivity Activity { get; } = activity;
        public IActivityExecutionContext ParentActivityExecutionContext => null!;
        public CancellationToken CancellationToken => CancellationToken.None;

        public TService GetRequiredService<TService>() where TService : notnull =>
            serviceProvider.GetRequiredService<TService>();

        public IAsyncEnumerable<ActivityOutputs> GetActivityOutputs() => AsyncEnumerable.Empty<ActivityOutputs>();

        public void SetOutcomes(string[] outcomes)
        {
        }

        public IEnumerable<string> GetOutcomes() => [];

        public void CreateBookmark(ActivityBookmarkRequest request)
        {
        }

        public IReadOnlyCollection<ActivityBookmarkRequest> GetBookmarkRequests() => [];
    }
}
