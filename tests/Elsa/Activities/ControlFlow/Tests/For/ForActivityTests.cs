using System.Text.Json;
using Elsa.Activities.For.Exceptions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;

namespace Elsa.Activities.For.Tests;

public sealed class ForActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    public void Dispose() => _serviceProvider.Dispose();

    [Fact]
    public async Task Execute_SchedulesBodyAtStartIndex_WithIterationProvenance()
    {
        var context = NewContext(start: 2, end: 5, step: 1);

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-body", request.ExecutableNodeId);
        Assert.Equal("actexec-for", request.SchedulingActivityExecutionId);
        Assert.Equal("2", request.SchedulingProvenance.IterationId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task Execute_CompletesWithoutSchedulingBody_WhenRangeIsEmpty()
    {
        var context = NewContext(start: 3, end: 3, step: 1);

        await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task Execute_CompletesWithoutSchedulingBody_WhenBodyIsAbsent()
    {
        var context = NewContext(start: 0, end: 3, step: 1, includeBody: false);

        await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task Execute_Faults_WhenStepIsZero()
    {
        var context = NewContext(start: 0, end: 5, step: 0);

        await Assert.ThrowsAsync<ForExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task OnChildCompleted_SchedulesNextIndex_WhenStillInRange()
    {
        var context = NewContext(start: 0, end: 3, step: 1);

        await ((ForActivity)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-body-0", "node-body", [ActivityOutcomes.Done], completedChildIterationId: "0"));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-body", request.ExecutableNodeId);
        Assert.Equal("1", request.SchedulingProvenance.IterationId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task OnChildCompleted_Completes_WhenRangeExhausted()
    {
        var context = NewContext(start: 0, end: 3, step: 1);

        await ((ForActivity)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-body-2", "node-body", [ActivityOutcomes.Done], completedChildIterationId: "2"));

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_BreakOutcome_EndsLoopEarly()
    {
        var context = NewContext(start: 0, end: 10, step: 1);

        await ((ForActivity)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-body-0", "node-body", [ForActivity.BreakOutcome], completedChildIterationId: "0"));

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_NegativeStep_CountsDown()
    {
        var context = NewContext(start: 5, end: 0, step: -1);

        await ((ForActivity)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "actexec-body-5", "node-body", [ActivityOutcomes.Done], completedChildIterationId: "5"));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("4", request.SchedulingProvenance.IterationId);
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenIterationIdMissing()
    {
        var context = NewContext(start: 0, end: 3, step: 1);

        await Assert.ThrowsAsync<ForExecutionException>(() => new ForActivity()
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-body", "node-body", [ActivityOutcomes.Done]))
            .AsTask());
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenCompletedChildIsNotBody()
    {
        var context = NewContext(start: 0, end: 3, step: 1);

        await Assert.ThrowsAsync<ForExecutionException>(() => new ForActivity()
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-x", "node-x", [ActivityOutcomes.Done], completedChildIterationId: "0"))
            .AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenRuntimeContextIsMissing()
    {
        var context = new NonRuntimeActivityExecutionContext(_serviceProvider, new ForActivity());

        await Assert.ThrowsAsync<ForExecutionException>(() => ((IActivity)new ForActivity()).ExecuteAsync(context).AsTask());
    }

    private async ValueTask ExecuteAsync(SimpleActivityExecutionContext context) =>
        await ((IActivity)context.Activity).ExecuteAsync(context);

    private SimpleActivityExecutionContext NewContext(int start, int end, int step, bool includeBody = true)
    {
        var activity = new ForActivity
        {
            Id = "actexec-for",
            NodeId = "node-for",
            Start = start,
            End = end,
            Step = step
        };

        var context = new SimpleActivityExecutionContext(
            _serviceProvider,
            activity,
            CancellationToken.None,
            "wfexec-1",
            NewIdentity(),
            NewWorkItem(),
            NewForNode(includeBody),
            NewRunningState());
        return context;
    }

    private static ExecutableNode NewForNode(bool includeBody)
    {
        var childSlots = new List<ExecutableChildSlot>();
        if (includeBody)
            childSlots.Add(new ExecutableChildSlot(ForActivity.BodySlotName, [NewNode("node-body")]));

        return NewNode(
            "node-for",
            activityType: "for",
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                ForActivity.StructureKind,
                ForActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = includeBody ? "node-body" : null })));
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

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution("actexec-for", "wfexec-1", "node-for", "authored-for", "for", "1.0.0"),
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
            workItemId: "work-for",
            workflowExecutionId: "wfexec-1",
            commandId: "command-for",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:for",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

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
