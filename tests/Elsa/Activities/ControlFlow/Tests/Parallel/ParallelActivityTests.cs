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
/// Fork behavior (Execute) and the fault-aware join (OnChildCompletedAsync / OnChildFaultedAsync) are asserted
/// directly; the join's per-branch terminal counts are fed by an in-memory
/// <see cref="IActivityExecutionStateStore"/> stub.
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

    private ParallelActivity NewActivity() => new(_store);

    [Fact]
    public async Task Execute_ForksAllBranches_WithDistinctBranchIdsAndParentLinkage_AndDefers()
    {
        var context = NewContext();

        var continuation = await ExecuteAsync(context);

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
        Assert.True(continuation.IsDeferred);
    }

    [Fact]
    public async Task OnChildCompleted_Defers_WhileBranchesOutstanding()
    {
        var context = NewContext();
        // Only one of three branches completed so far.
        _store.SeedCompletedBranch("node-a");

        var continuation = await CompleteAsync(context, "node-a");

        Assert.True(continuation.IsDeferred);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesWithDone_WhenAllBranchesFinished()
    {
        var context = NewContext();
        _store.SeedCompletedBranch("node-a");
        _store.SeedCompletedBranch("node-b");
        _store.SeedCompletedBranch("node-c");

        var continuation = await CompleteAsync(context, "node-c");

        Assert.True(continuation.IsComplete);
        Assert.Equal(ActivityOutcomes.Done, continuation.OutcomeName);
    }

    [Fact]
    public async Task OnChildCompleted_IgnoresActivityUnderDifferentParent_WithSameBranchNodeId()
    {
        var context = NewContext();
        // Two of three branches of THIS composite completed.
        _store.SeedCompletedBranch("node-a");
        _store.SeedCompletedBranch("node-b");
        // A completed activity elsewhere in the same workflow that reuses branch node id "node-c" but is parented by a
        // DIFFERENT composite. It must not be counted toward this join. If parent-scoping leaked it in, the count would
        // reach the threshold of 3 and wrongly complete; correct behavior is to keep deferring.
        _store.SeedForeignParentActivity("node-c", parentActivityExecutionId: "actexec-other-composite");

        var continuation = await CompleteAsync(context, "node-b");

        Assert.True(continuation.IsDeferred);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesAtThreshold_WhenSubsetConfigured()
    {
        var context = NewContext(threshold: 2);
        _store.SeedCompletedBranch("node-a");
        _store.SeedCompletedBranch("node-b");

        var continuation = await CompleteAsync(context, "node-b");

        Assert.True(continuation.IsComplete);
        Assert.Equal(ActivityOutcomes.Done, continuation.OutcomeName);
    }

    [Fact]
    public async Task OnChildCompleted_DoesNotMiscount_WhenChildCompletesTwice()
    {
        var context = NewContext();
        // Same branch node persisted twice must count once: still below threshold (3), so defer.
        _store.SeedCompletedBranch("node-a");
        _store.SeedCompletedBranch("node-a");

        var continuation = await CompleteAsync(context, "node-a");

        Assert.True(continuation.IsDeferred);
    }

    [Fact]
    public async Task Execute_CompletesWithDone_WhenNoBranches()
    {
        var context = NewContext(includeBranches: false);

        var continuation = await ExecuteAsync(context);

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(continuation.IsComplete);
        Assert.Equal(ActivityOutcomes.Done, continuation.OutcomeName);
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
    public async Task OnChildFaulted_FaultsComposite_WhenJoinNoLongerSatisfiable()
    {
        var context = NewContext();
        // Default threshold = all 3 branches; one faulted branch makes the success threshold unreachable, so
        // the join returns an authored structural fault for the engine to record.
        _store.SeedFaultedBranch("node-a");

        var continuation = await FaultAsync(context, "node-a");

        Assert.Equal(RuntimeStructuralContinuationKind.Fault, continuation.Kind);
        Assert.Equal("parallel.join.unsatisfied", continuation.Fault!.Code);
    }

    [Fact]
    public async Task OnChildFaulted_Defers_WhenJoinStillReachable()
    {
        var context = NewContext(threshold: 2);
        // Threshold 2 of 3: one faulted branch still leaves two branches that can satisfy the join, so defer.
        _store.SeedFaultedBranch("node-a");

        var continuation = await FaultAsync(context, "node-a");

        Assert.True(continuation.IsDeferred);
    }

    [Fact]
    public async Task OnChildCompleted_CompletesAtThreshold_DespiteFaultedBranch()
    {
        var context = NewContext(threshold: 2);
        // One branch faulted but the other two succeeded: the threshold is met, so the composite completes.
        _store.SeedFaultedBranch("node-a");
        _store.SeedCompletedBranch("node-b");
        _store.SeedCompletedBranch("node-c");

        var continuation = await CompleteAsync(context, "node-c");

        Assert.True(continuation.IsComplete);
        Assert.Equal(ActivityOutcomes.Done, continuation.OutcomeName);
    }

    [Fact]
    public async Task OnChildFaulted_Throws_WhenFaultedChildIsNotABranch()
    {
        var context = NewContext();

        await Assert.ThrowsAsync<ParallelExecutionException>(() => ((ParallelActivity)context.Activity)
            .OnChildFaultedAsync(new ActivityChildFaultedContext(context, "actexec-x", "node-other", "incident-1"))
            .AsTask());
    }

    private static ValueTask<RuntimeStructuralContinuation> ExecuteAsync(SimpleActivityExecutionContext context) =>
        ((IRuntimeStructuralActivity)context.Activity).ExecuteStructureAsync(context);

    private static ValueTask<RuntimeStructuralContinuation> CompleteAsync(SimpleActivityExecutionContext context, string completedNodeId) =>
        ((ParallelActivity)context.Activity).OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, $"actexec-{completedNodeId}", completedNodeId, [ActivityOutcomes.Done]));

    private static ValueTask<RuntimeStructuralContinuation> FaultAsync(SimpleActivityExecutionContext context, string faultedNodeId) =>
        ((ParallelActivity)context.Activity).OnChildFaultedAsync(
            new ActivityChildFaultedContext(context, $"actexec-{faultedNodeId}", faultedNodeId, "incident-1"));

    private SimpleActivityExecutionContext NewContext(bool includeBranches = true, int? threshold = null) =>
        new(
            new ParallelActivity(_store),
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
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
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
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
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

        public void SeedCompletedBranch(string executableNodeId) => SeedBranch(executableNodeId, ActivityExecutionStatus.Completed);

        public void SeedFaultedBranch(string executableNodeId) => SeedBranch(executableNodeId, ActivityExecutionStatus.Faulted);

        private void SeedBranch(string executableNodeId, ActivityExecutionStatus status) =>
            SeedActivity(executableNodeId, status, ParallelExecutionId);

        /// <summary>
        /// Seeds an activity state parented by a DIFFERENT composite than the Parallel under test (a sibling elsewhere in the
        /// same workflow). It must never be counted by the parallel join: with the parent-scoped read it is not even returned
        /// by <see cref="ListByParentAsync"/>. This guards that parent-scoping neither widens nor narrows the counted set.
        /// </summary>
        public void SeedForeignParentActivity(string executableNodeId, string parentActivityExecutionId) =>
            SeedActivity(executableNodeId, ActivityExecutionStatus.Completed, parentActivityExecutionId);

        private void SeedActivity(string executableNodeId, ActivityExecutionStatus status, string parentActivityExecutionId)
        {
            _states.Add(new ActivityExecutionState(
                Execution: new ActivityExecution($"actexec-{executableNodeId}-{_states.Count}", "wfexec-1", executableNodeId, $"authored-{executableNodeId}", "test/probe", "1.0.0"),
                Status: status,
                SubStatus: null,
                ScheduledAt: DateTimeOffset.UnixEpoch,
                StartedAt: DateTimeOffset.UnixEpoch,
                CompletedAt: DateTimeOffset.UnixEpoch,
                SchedulingActivityExecutionId: parentActivityExecutionId,
                ParentActivityExecutionId: parentActivityExecutionId,
                BranchId: $"{parentActivityExecutionId}:parallel-branch:{executableNodeId}",
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
            new(_states.Where(s => s.Execution.WorkflowExecutionId == workflowExecutionId).ToArray());

        public ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListByParentAsync(string workflowExecutionId, string parentActivityExecutionId, CancellationToken cancellationToken = default) =>
            new(_states
                .Where(s =>
                    s.Execution.WorkflowExecutionId == workflowExecutionId &&
                    StringComparer.Ordinal.Equals(s.ParentActivityExecutionId, parentActivityExecutionId))
                .ToArray());
    }

}
