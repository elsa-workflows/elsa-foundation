using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence.Exceptions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

public sealed class SequenceActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task ExecuteAsync_CompletesWhenSlotHasNoChildren()
    {
        var context = NewContext(NewSequenceNode([]));

        await ExecuteAsync(context);

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
        Assert.Empty(context.GetChildActivityScheduleRequests());
    }

    [Fact]
    public async Task ExecuteAsync_CompletesWhenSlotIsMissing()
    {
        var context = NewContext(NewNode("node-sequence", activityType: "sequence"));

        await ExecuteAsync(context);

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
        Assert.Empty(context.GetChildActivityScheduleRequests());
    }

    [Fact]
    public async Task ExecuteAsync_SchedulesFirstChild()
    {
        var context = NewContext(NewSequenceNode([NewNode("node-a"), NewNode("node-b")]));

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-a", request.ExecutableNodeId);
        Assert.Equal("actexec-sequence", request.SchedulingActivityExecutionId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task ExecuteAsync_SchedulesFirstChildFromStructureOrder()
    {
        var context = NewContext(NewSequenceNode(
            [NewNode("node-a"), NewNode("node-b")],
            ["node-b", "node-a"]));

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-b", request.ExecutableNodeId);
    }

    [Fact]
    public async Task OnChildCompletedAsync_SchedulesNextChild()
    {
        var context = NewContext(NewSequenceNode([NewNode("node-a"), NewNode("node-b"), NewNode("node-c")]));
        var sequence = new SequenceActivity();

        await sequence.OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-a", "node-a", [ActivityOutcomes.Done]));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-b", request.ExecutableNodeId);
        Assert.Equal("actexec-a", request.SchedulingActivityExecutionId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task OnChildCompletedAsync_CompletesAfterLastChild()
    {
        var context = NewContext(NewSequenceNode([NewNode("node-a"), NewNode("node-b")]));
        var sequence = new SequenceActivity();

        await sequence.OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-b", "node-b", [ActivityOutcomes.Done]));

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompletedAsync_ThrowsWhenCompletedChildIsUnknown()
    {
        var context = NewContext(NewSequenceNode([NewNode("node-a")]));
        var sequence = new SequenceActivity();

        await Assert.ThrowsAsync<SequenceExecutionException>(() => sequence
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-missing", "missing", [ActivityOutcomes.Done]))
            .AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenStructureReferencesMissingChild()
    {
        var context = NewContext(NewSequenceNode([NewNode("node-a")], ["node-a", "missing"]));

        await Assert.ThrowsAsync<SequenceExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenStructureContainsDuplicateChild()
    {
        var context = NewContext(NewSequenceNode([NewNode("node-a")], ["node-a", "node-a"]));

        await Assert.ThrowsAsync<SequenceExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenRuntimeContextIsMissing()
    {
        var context = new NonRuntimeActivityExecutionContext(_serviceProvider, new SequenceActivity());

        await Assert.ThrowsAsync<SequenceExecutionException>(() => ((IActivity)new SequenceActivity()).ExecuteAsync(context).AsTask());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private ValueTask ExecuteAsync(SimpleActivityExecutionContext context) =>
        ((IActivity)new SequenceActivity()).ExecuteAsync(context);

    private SimpleActivityExecutionContext NewContext(ExecutableNode executableNode)
    {
        var activity = new SequenceActivity { Id = "actexec-sequence", NodeId = "node-sequence" };
        return new SimpleActivityExecutionContext(
            _serviceProvider,
            activity,
            CancellationToken.None,
            "wfexec-1",
            NewIdentity(),
            NewWorkItem(),
            executableNode,
            NewRunningState());
    }

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution("actexec-sequence", "wfexec-1", "node-sequence", "authored-sequence", "sequence", "1.0.0"),
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
            workItemId: "work-sequence",
            workflowExecutionId: "wfexec-1",
            commandId: "command-sequence",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:sequence",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static ExecutableNode NewSequenceNode(IReadOnlyCollection<ExecutableNode> children, IReadOnlyCollection<string>? orderedChildIds = null) =>
        NewNode(
            "node-sequence",
            activityType: "sequence",
            childSlots:
            [
                new ExecutableChildSlot(
                    SequenceActivity.ActivitiesSlotName,
                    children)
            ],
            structure: NewSequenceStructure(orderedChildIds ?? children.Select(child => child.ExecutableNodeId).ToArray()));

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

    private static ExecutableActivityStructure NewSequenceStructure(IReadOnlyCollection<string> activities) =>
        new(
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new { activities }));

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class NonRuntimeActivityExecutionContext(IServiceProvider serviceProvider, IActivity activity) : IActivityExecutionContext
    {
        public IExpressionExecutionContext ExpressionExecutionContext => null!;
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
