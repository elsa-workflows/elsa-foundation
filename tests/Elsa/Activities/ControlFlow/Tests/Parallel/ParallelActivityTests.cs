using System.Text.Json;
using Elsa.Activities.Parallel.Exceptions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.Parallel.Tests;

/// <summary>
/// Unit coverage for the <c>Parallel</c> composite against the <see cref="SimpleActivityExecutionContext"/>.
/// Fork behavior (Execute) and join behavior (OnChildCompletedAsync) are asserted directly; the join's
/// completed-branch count is fed by an in-memory <see cref="IActivityExecutionStateStore"/> stub.
/// </summary>
public sealed class ParallelActivityTests : IDisposable
{
    private const string ParallelNodeId = "node-parallel";
    private const string ParallelExecutionId = "actexec-parallel";
    private static readonly (string Name, string Node)[] Branches = [("a", "node-a"), ("b", "node-b"), ("c", "node-c")];

    private readonly FakeActivityExecutionStateStore _store = new();
    private readonly ServiceProvider _serviceProvider;

    public ParallelActivityTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityExecutionStateStore>(_store);
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose() => _serviceProvider.Dispose();

    [Fact]
    public async Task Execute_ForksAllBranches_WithDistinctBranchIdsAndParentLinkage_AndDefers()
    {
        var context = NewContext();

        await ExecuteAsync(context);

        var requests = context.GetChildActivityScheduleRequests();
        Assert.Equal(3, requests.Count);
        Assert.Equal(new[] { "node-a", "node-b", "node-c" }, requests.Select(r => r.ExecutableNodeId).ToArray());

        // Each branch is forked under the composite as parent and a distinct BranchId.
        foreach (var request in requests)
        {
            Assert.Equal(ParallelExecutionId, request.SchedulingProvenance.ParentActivityExecutionId);
            Assert.Equal(ParallelExecutionId, request.SchedulingActivityExecutionId);
            Assert.Equal($"{ParallelExecutionId}:parallel-branch:{request.ExecutableNodeId}", request.SchedulingProvenance.BranchId);
        }

        var branchIds = requests.Select(r => r.SchedulingProvenance.BranchId).ToArray();
        Assert.Equal(branchIds.Length, branchIds.Distinct().Count());

        // Fork defers; the join completes the composite once branches finish.
        Assert.True(context.CompositeCompletionDeferred);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task OnChildCompleted_Defers_WhileBranchesOutstanding()
    {
        var context = NewContext();
        // Only one of three branches completed so far.
        _store.SeedCompletedBranch("node-a");

        await CompleteAsync(context, "node-a");

        Assert.True(context.CompositeCompletionDeferred);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithDone_WhenAllBranchesFinished()
    {
        var context = NewContext();
        _store.SeedCompletedBranch("node-a");
        _store.SeedCompletedBranch("node-b");
        _store.SeedCompletedBranch("node-c");

        await CompleteAsync(context, "node-c");

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesAtThreshold_WhenSubsetConfigured()
    {
        var context = NewContext(threshold: 2);
        _store.SeedCompletedBranch("node-a");
        _store.SeedCompletedBranch("node-b");

        await CompleteAsync(context, "node-b");

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_DoesNotMiscount_WhenChildCompletesTwice()
    {
        var context = NewContext();
        // Same branch node persisted twice must count once: still below threshold (3), so defer.
        _store.SeedCompletedBranch("node-a");
        _store.SeedCompletedBranch("node-a");

        await CompleteAsync(context, "node-a");

        Assert.True(context.CompositeCompletionDeferred);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task Execute_CompletesWithDone_WhenNoBranches()
    {
        var context = NewContext(includeBranches: false);

        await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task OnChildCompleted_Throws_WhenCompletedChildIsNotABranch()
    {
        var context = NewContext();

        await Assert.ThrowsAsync<ParallelExecutionException>(() => ((ParallelActivity)context.Activity)
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-x", "node-other", [ActivityOutcomes.Done]))
            .AsTask());
    }

    [Fact]
    public async Task Execute_Throws_WhenRuntimeContextIsMissing()
    {
        await Assert.ThrowsAsync<ParallelExecutionException>(() => ((IActivity)new ParallelActivity())
            .ExecuteAsync(new NonRuntimeActivityExecutionContext(_serviceProvider, new ParallelActivity()))
            .AsTask());
    }

    private static ValueTask ExecuteAsync(SimpleActivityExecutionContext context) =>
        ((IActivity)context.Activity).ExecuteAsync(context);

    private static ValueTask CompleteAsync(SimpleActivityExecutionContext context, string completedNodeId) =>
        ((ParallelActivity)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, $"actexec-{completedNodeId}", completedNodeId, [ActivityOutcomes.Done]));

    private SimpleActivityExecutionContext NewContext(bool includeBranches = true, int? threshold = null) =>
        new(
            _serviceProvider,
            new ParallelActivity { Id = ParallelExecutionId, NodeId = ParallelNodeId },
            CancellationToken.None,
            "wfexec-1",
            NewIdentity(),
            NewWorkItem(),
            NewParallelNode(includeBranches, threshold),
            NewRunningState());

    private static ExecutableNode NewParallelNode(bool includeBranches, int? threshold)
    {
        var childSlots = includeBranches
            ? Branches.Select(b => new ExecutableChildSlot(ParallelActivity.BranchSlotName(b.Name), [NewBranchNode(b.Node)])).ToArray()
            : [];

        return new ExecutableNode(
            executableNodeId: ParallelNodeId,
            authoredActivityId: "authored-parallel",
            activityType: typeof(ParallelActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                ParallelActivity.StructureKind,
                ParallelActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    branches = (includeBranches ? Branches : []).Select(b => new { name = b.Name, activity = b.Node }).ToArray(),
                    threshold
                })));
    }

    private static ExecutableNode NewBranchNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/probe",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution(ParallelExecutionId, "wfexec-1", ParallelNodeId, "authored-parallel", "parallel", "1.0.0"),
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
            workItemId: "work-parallel",
            workflowExecutionId: "wfexec-1",
            commandId: "command-parallel",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:parallel",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class FakeActivityExecutionStateStore : IActivityExecutionStateStore
    {
        private readonly List<ActivityExecutionState> _states = [];

        public void SeedCompletedBranch(string executableNodeId)
        {
            _states.Add(new ActivityExecutionState(
                Execution: new ActivityExecution($"actexec-{executableNodeId}-{_states.Count}", "wfexec-1", executableNodeId, $"authored-{executableNodeId}", "test/probe", "1.0.0"),
                Status: ActivityExecutionStatus.Completed,
                SubStatus: null,
                ScheduledAt: DateTimeOffset.UnixEpoch,
                StartedAt: DateTimeOffset.UnixEpoch,
                CompletedAt: DateTimeOffset.UnixEpoch,
                SchedulingActivityExecutionId: ParallelExecutionId,
                ParentActivityExecutionId: ParallelExecutionId,
                BranchId: $"{ParallelExecutionId}:parallel-branch:{executableNodeId}",
                IterationId: null,
                CallStackDepth: 0,
                BookmarkIds: [],
                IncidentIds: [],
                FaultCount: 0,
                AggregateFaultCount: 0,
                Metadata: new Dictionary<string, string>()));
        }

        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
        {
            _states.Add(state);
            return new(state);
        }

        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            new(_states.FirstOrDefault(s => s.Execution.ActivityExecutionId == activityExecutionId));

        public ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            new(_states.ToArray());
    }

    private sealed class NonRuntimeActivityExecutionContext(IServiceProvider serviceProvider, IActivity activity) : IActivityExecutionContext
    {
        public Elsa.Expressions.Core.Contracts.IExpressionExecutionContext ExpressionExecutionContext => null!;
        public IActivity Activity { get; } = activity;
        public IActivityExecutionContext ParentActivityExecutionContext => null!;
        public CancellationToken CancellationToken => CancellationToken.None;

        public TService GetRequiredService<TService>() where TService : notnull =>
            serviceProvider.GetRequiredService<TService>();

        public T? Get<T>(InputArgument<T>? input) => default;
        public void Set<T>(OutputArgument<T>? output, T? value, string? outputName = null) { }
        public IAsyncEnumerable<ActivityOutputs> GetActivityOutputs() => AsyncEnumerable.Empty<ActivityOutputs>();
        public void SetOutcomes(string[] outcomes) { }
        public IEnumerable<string> GetOutcomes() => [];
        public void CreateBookmark(ActivityBookmarkRequest request) { }
        public IReadOnlyCollection<ActivityBookmarkRequest> GetBookmarkRequests() => [];
    }
}
