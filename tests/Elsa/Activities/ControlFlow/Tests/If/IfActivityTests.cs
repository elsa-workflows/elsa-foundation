using System.Text.Json;
using Elsa.Activities.If.Exceptions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IfActivity = Elsa.Activities.If.Activities.If;

namespace Elsa.Activities.If.Tests;

public sealed class IfActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_SchedulesThenBranch_WhenConditionIsTrue()
    {
        var context = NewContext(NewIfNode(then: NewNode("node-then"), @else: NewNode("node-else")), condition: true);

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-then", request.ExecutableNodeId);
        Assert.Equal("actexec-if", request.SchedulingActivityExecutionId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task Execute_SchedulesElseBranch_WhenConditionIsFalse()
    {
        var context = NewContext(NewIfNode(then: NewNode("node-then"), @else: NewNode("node-else")), condition: false);

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-else", request.ExecutableNodeId);
    }

    [Fact]
    public async Task Execute_CompletesWithTrueOutcome_WhenConditionIsTrueAndThenBranchIsAbsent()
    {
        var context = NewContext(NewIfNode(then: null, @else: NewNode("node-else")), condition: true);

        await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.True], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task Execute_CompletesWithFalseOutcome_WhenConditionIsFalseAndElseBranchIsAbsent()
    {
        var context = NewContext(NewIfNode(then: NewNode("node-then"), @else: null), condition: false);

        await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.False], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithTrueOutcome_AfterThenBranch()
    {
        var context = NewContext(NewIfNode(then: NewNode("node-then"), @else: NewNode("node-else")), condition: true);

        await new IfActivity().OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-then", "node-then", [ActivityOutcomes.Done]));

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.True], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithFalseOutcome_AfterElseBranch()
    {
        var context = NewContext(NewIfNode(then: NewNode("node-then"), @else: NewNode("node-else")), condition: false);

        await new IfActivity().OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-else", "node-else", [ActivityOutcomes.Done]));

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.False], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_TakenBranchBreaks_CompletesWithBreak_InsteadOfTrueOrFalse()
    {
        // The taken (Then) branch completes with Break: If completes with Break instead of True so the
        // outcome bubbles to the enclosing loop (#299).
        var context = NewContext(NewIfNode(then: NewNode("node-then"), @else: NewNode("node-else")), condition: true);

        await new IfActivity().OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-then", "node-then", [ActivityOutcomes.Break]));

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Break], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenCompletedChildIsNotABranch()
    {
        var context = NewContext(NewIfNode(then: NewNode("node-then"), @else: NewNode("node-else")), condition: true);

        await Assert.ThrowsAsync<IfExecutionException>(() => new IfActivity()
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-x", "node-x", [ActivityOutcomes.Done]))
            .AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenSlotChildDoesNotMatchStructure()
    {
        // Slot carries a child the structure does not declare as the Then branch.
        var node = NewNode(
            "node-if",
            activityType: "if",
            childSlots:
            [
                new ExecutableChildSlot(IfActivity.ThenSlotName, [NewNode("slot-child")])
            ],
            structure: NewIfStructure(then: "declared-but-missing", @else: null));
        var context = NewContext(node, condition: true);

        await Assert.ThrowsAsync<IfExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenRuntimeContextIsMissing()
    {
        var context = new NonRuntimeActivityExecutionContext(_serviceProvider, new IfActivity());

        await Assert.ThrowsAsync<IfExecutionException>(() => ((IActivity)new IfActivity()).ExecuteAsync(context).AsTask());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private async ValueTask ExecuteAsync(SimpleActivityExecutionContext context) =>
        await ((IActivity)context.Activity).ExecuteAsync(context);

    private SimpleActivityExecutionContext NewContext(ExecutableNode executableNode, bool condition)
    {
        var conditionInput = new InputArgument<bool>(new Variable("Condition", condition));
        var activity = new IfActivity { Id = "actexec-if", NodeId = "node-if", Condition = conditionInput };
        var context = new SimpleActivityExecutionContext(
            _serviceProvider,
            activity,
            CancellationToken.None,
            "wfexec-1",
            NewIdentity(),
            NewWorkItem(),
            executableNode,
            NewRunningState());
        context.Set(conditionInput.MemoryBlockReference(), condition);
        return context;
    }

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution("actexec-if", "wfexec-1", "node-if", "authored-if", "if", "1.0.0"),
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
            workItemId: "work-if",
            workflowExecutionId: "wfexec-1",
            commandId: "command-if",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:if",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static ExecutableNode NewIfNode(ExecutableNode? then, ExecutableNode? @else)
    {
        var childSlots = new List<ExecutableChildSlot>();
        if (then is not null)
            childSlots.Add(new ExecutableChildSlot(IfActivity.ThenSlotName, [then]));
        if (@else is not null)
            childSlots.Add(new ExecutableChildSlot(IfActivity.ElseSlotName, [@else]));

        return NewNode(
            "node-if",
            activityType: "if",
            childSlots: childSlots,
            structure: NewIfStructure(then?.ExecutableNodeId, @else?.ExecutableNodeId));
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
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: structure);

    private static ExecutableActivityStructure NewIfStructure(string? then, string? @else) =>
        new(
            IfActivity.StructureKind,
            IfActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new { then, @else }));

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class NonRuntimeActivityExecutionContext(IServiceProvider serviceProvider, IActivity activity) : IActivityExecutionContext
    {
        public Elsa.Expressions.Core.Contracts.IExpressionExecutionContext ExpressionExecutionContext => null!;
        public IActivity Activity { get; } = activity;
        public IActivityExecutionContext ParentActivityExecutionContext => null!;
        public CancellationToken CancellationToken => CancellationToken.None;

        public TService GetRequiredService<TService>() where TService : notnull =>
            serviceProvider.GetRequiredService<TService>();

        public T? Get<T>(InputArgument<T>? input) => default;

        public void Set<T>(OutputArgument<T>? output, T? value, string? outputName = null)
        {
        }

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
