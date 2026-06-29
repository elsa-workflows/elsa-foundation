using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Do.Exceptions;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DoActivity = Elsa.Activities.Do.Activities.Do;

namespace Elsa.Activities.Do.Tests;

public sealed class DoActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_SchedulesBody_WhenConditionTrueOnEntry()
    {
        var context = NewContext(NewDoNode(body: NewNode("node-body")), condition: true);

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-body", request.ExecutableNodeId);
        Assert.Equal("actexec-do", request.SchedulingActivityExecutionId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task Execute_SchedulesBody_EvenWhenConditionFalseOnEntry()
    {
        // Post-test loop: the body runs at least once, so a false entry condition still schedules it.
        var context = NewContext(NewDoNode(body: NewNode("node-body")), condition: false);

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-body", request.ExecutableNodeId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task Execute_SchedulesBody_WithDistinctIterationIdInProvenance()
    {
        var context = NewContext(NewDoNode(body: NewNode("node-body")), condition: true);

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.False(string.IsNullOrEmpty(request.SchedulingProvenance.IterationId));
        Assert.Equal("actexec-do:iter:0", request.SchedulingProvenance.IterationId);
        Assert.Equal(request.SchedulingProvenance.IterationId, request.Metadata["do.iterationId"]);
    }

    [Fact]
    public async Task Execute_CompletesWithDone_WhenBodyIsAbsent()
    {
        var context = NewContext(NewDoNode(body: null), condition: true);

        await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_ReschedulesBody_WhileConditionHolds()
    {
        var context = NewContext(NewDoNode(body: NewNode("node-body")), condition: true);

        await ((IActivityChildCompletionHandler)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-body-1", "node-body", [ActivityOutcomes.Done]));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-body", request.ExecutableNodeId);
        Assert.False(context.CompositeCompletionRequested);
        // The next pass uses an iteration id seeded from the completed child, distinct from the first pass.
        Assert.Equal("actexec-do:iter:actexec-body-1", request.SchedulingProvenance.IterationId);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithDone_WhenConditionNoLongerHolds()
    {
        var context = NewContext(NewDoNode(body: NewNode("node-body")), condition: false);

        await ((IActivityChildCompletionHandler)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-body-1", "node-body", [ActivityOutcomes.Done]));

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_BreakOutcome_EndsLoopEarly_WithoutRecheckingCondition()
    {
        // Condition is true, but a Break outcome ends the loop early all the same.
        var context = NewContext(NewDoNode(body: NewNode("node-body")), condition: true);

        await ((IActivityChildCompletionHandler)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-body-1", "node-body", [DoActivity.BreakOutcome]));

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task SuccessivePasses_UseDistinctIterationIds()
    {
        // The unconditional first pass and a subsequent body-completion pass must publish distinct ids.
        var entryContext = NewContext(NewDoNode(body: NewNode("node-body")), condition: true);
        await ExecuteAsync(entryContext);
        var entryIterationId = Assert.Single(entryContext.GetChildActivityScheduleRequests()).SchedulingProvenance.IterationId;

        var nextContext = NewContext(NewDoNode(body: NewNode("node-body")), condition: true);
        await ((IActivityChildCompletionHandler)nextContext.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(nextContext, "actexec-body-1", "node-body", [ActivityOutcomes.Done]));
        var nextIterationId = Assert.Single(nextContext.GetChildActivityScheduleRequests()).SchedulingProvenance.IterationId;

        Assert.NotEqual(entryIterationId, nextIterationId);
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenRuntimeContextIsMissing()
    {
        var context = new NonRuntimeActivityExecutionContext(_serviceProvider, new DoActivity());

        await Assert.ThrowsAsync<DoExecutionException>(() => new DoActivity()
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-x", "node-x", [ActivityOutcomes.Done]))
            .AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenSlotChildDoesNotMatchStructure()
    {
        // Slot carries a child the structure does not declare as the body branch.
        var node = NewNode(
            "node-do",
            activityType: "do",
            childSlots: [new ExecutableChildSlot(DoActivity.BodySlotName, [NewNode("slot-child")])],
            structure: NewDoStructure(body: "declared-but-missing"));
        var context = NewContext(node, condition: true);

        await Assert.ThrowsAsync<DoExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenRuntimeContextIsMissing()
    {
        var context = new NonRuntimeActivityExecutionContext(_serviceProvider, new DoActivity());

        await Assert.ThrowsAsync<DoExecutionException>(() => ((IActivity)new DoActivity()).ExecuteAsync(context).AsTask());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private async ValueTask ExecuteAsync(SimpleActivityExecutionContext context) =>
        await ((IActivity)context.Activity).ExecuteAsync(context);

    private SimpleActivityExecutionContext NewContext(ExecutableNode executableNode, bool condition)
    {
        var conditionInput = new InputArgument<bool>(new Variable("Condition", condition));
        var activity = new DoActivity { Id = "actexec-do", NodeId = "node-do", Condition = conditionInput };
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
            Execution: new ActivityExecution("actexec-do", "wfexec-1", "node-do", "authored-do", "do", "1.0.0"),
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
            workItemId: "work-do",
            workflowExecutionId: "wfexec-1",
            commandId: "command-do",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:do",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static ExecutableNode NewDoNode(ExecutableNode? body)
    {
        var childSlots = new List<ExecutableChildSlot>();
        if (body is not null)
            childSlots.Add(new ExecutableChildSlot(DoActivity.BodySlotName, [body]));

        return NewNode(
            "node-do",
            activityType: "do",
            childSlots: childSlots,
            structure: NewDoStructure(body?.ExecutableNodeId));
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

    private static ExecutableActivityStructure NewDoStructure(string? body) =>
        new(
            DoActivity.StructureKind,
            DoActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new { body }));

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
