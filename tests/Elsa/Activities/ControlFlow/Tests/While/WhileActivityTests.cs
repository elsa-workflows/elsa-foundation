using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.While.Exceptions;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.While.Tests;

public sealed class WhileActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Execute_SchedulesBody_WhenConditionTrueOnEntry()
    {
        var context = NewContext(NewWhileNode(body: NewNode("node-body")), condition: true);

        var continuation = await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-body", request.ExecutableNodeId);
        Assert.Equal("actexec-while", request.SchedulingActivityExecutionId);
        Assert.True(continuation.IsDeferred);
    }

    [Fact]
    public async Task Execute_SchedulesBody_WithDistinctIterationIdInProvenance()
    {
        var context = NewContext(NewWhileNode(body: NewNode("node-body")), condition: true);

        var continuation = await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.False(string.IsNullOrEmpty(request.SchedulingProvenance.IterationId));
        Assert.Equal("actexec-while:iter:0", request.SchedulingProvenance.IterationId);
        Assert.Equal(request.SchedulingProvenance.IterationId, request.Metadata["while.iterationId"]);
    }

    [Fact]
    public async Task Execute_CompletesWithDone_WhenConditionFalseOnEntry()
    {
        // Condition false on entry: the body never runs and the composite completes immediately.
        var context = NewContext(NewWhileNode(body: NewNode("node-body")), condition: false);

        var continuation = await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(continuation.IsComplete);
        Assert.Equal(ActivityOutcomes.Done, continuation.OutcomeName);
    }

    [Fact]
    public async Task Execute_CompletesWithDone_WhenBodyIsAbsent()
    {
        var context = NewContext(NewWhileNode(body: null), condition: true);

        var continuation = await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(continuation.IsComplete);
        Assert.Equal(ActivityOutcomes.Done, continuation.OutcomeName);
    }

    [Fact]
    public async Task OnChildCompleted_ReschedulesBody_WhileConditionHolds()
    {
        var context = NewContext(NewWhileNode(body: NewNode("node-body")), condition: true);

        var continuation = await ((IRuntimeActivityChildCompletionHandler)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-body-1", "node-body", [ActivityOutcomes.Done]));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-body", request.ExecutableNodeId);
        Assert.True(continuation.IsDeferred);
        // The next pass uses an iteration id seeded from the completed child, distinct from the entry pass.
        Assert.Equal("actexec-while:iter:actexec-body-1", request.SchedulingProvenance.IterationId);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithDone_WhenConditionNoLongerHolds()
    {
        var context = NewContext(NewWhileNode(body: NewNode("node-body")), condition: false);

        var continuation = await ((IRuntimeActivityChildCompletionHandler)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-body-1", "node-body", [ActivityOutcomes.Done]));

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(continuation.IsComplete);
        Assert.Equal(ActivityOutcomes.Done, continuation.OutcomeName);
    }

    [Fact]
    public async Task SuccessivePasses_UseDistinctIterationIds()
    {
        // Entry pass and a subsequent body-completion pass must publish distinct iteration ids.
        var entryContext = NewContext(NewWhileNode(body: NewNode("node-body")), condition: true);
        await ExecuteAsync(entryContext);
        var entryIterationId = Assert.Single(entryContext.GetChildActivityScheduleRequests()).SchedulingProvenance.IterationId;

        var nextContext = NewContext(NewWhileNode(body: NewNode("node-body")), condition: true);
        await ((IRuntimeActivityChildCompletionHandler)nextContext.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(nextContext, "actexec-body-1", "node-body", [ActivityOutcomes.Done]));
        var nextIterationId = Assert.Single(nextContext.GetChildActivityScheduleRequests()).SchedulingProvenance.IterationId;

        Assert.NotEqual(entryIterationId, nextIterationId);
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenCompletedChildIsNotBody()
    {
        // #381: a stray child-completion callback must throw a diagnosable exception (mirroring
        // For/ForEach/If/Switch/Parallel) instead of silently rescheduling the body or completing.
        var context = NewContext(NewWhileNode(body: NewNode("node-body")), condition: true);

        await Assert.ThrowsAsync<WhileExecutionException>(() => ((IRuntimeActivityChildCompletionHandler)context.Activity)
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-x", "node-x", [ActivityOutcomes.Done]))
            .AsTask());
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenRuntimeContextIsMissing()
    {
        var context = new NonRuntimeActivityExecutionContext(new WhileActivity());

        await Assert.ThrowsAsync<WhileExecutionException>(() => new WhileActivity()
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-x", "node-x", [ActivityOutcomes.Done]))
            .AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenSlotChildDoesNotMatchStructure()
    {
        // Slot carries a child the structure does not declare as the body branch.
        var node = NewNode(
            "node-while",
            activityType: "while",
            childSlots: [new ExecutableChildSlot(WhileActivity.BodySlotName, [NewNode("slot-child")])],
            structure: NewWhileStructure(body: "declared-but-missing"));
        var context = NewContext(node, condition: true);

        await Assert.ThrowsAsync<WhileExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private static ValueTask<RuntimeStructuralContinuation> ExecuteAsync(SimpleActivityExecutionContext context) =>
        ((IRuntimeStructuralActivity)context.Activity).ExecuteStructureAsync(context);

    private SimpleActivityExecutionContext NewContext(ExecutableNode executableNode, bool condition)
    {
        var activity = new WhileActivity { Condition = condition };
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
            Execution: new ActivityExecution("actexec-while", "wfexec-1", "node-while", "authored-while", "while", "1.0.0"),
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
            workItemId: "work-while",
            workflowExecutionId: "wfexec-1",
            commandId: "command-while",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:while",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static ExecutableNode NewWhileNode(ExecutableNode? body)
    {
        var childSlots = new List<ExecutableChildSlot>();
        if (body is not null)
            childSlots.Add(new ExecutableChildSlot(WhileActivity.BodySlotName, [body]));

        return NewNode(
            "node-while",
            activityType: "while",
            childSlots: childSlots,
            structure: NewWhileStructure(body?.ExecutableNodeId));
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
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: structure);

    private static ExecutableActivityStructure NewWhileStructure(string? body) =>
        new(
            WhileActivity.StructureKind,
            WhileActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new { body }));

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class NonRuntimeActivityExecutionContext(IActivity activity) : IActivityExecutionContext
    {
        public IActivity Activity { get; } = activity;
        public CancellationToken CancellationToken => CancellationToken.None;
    }
}
