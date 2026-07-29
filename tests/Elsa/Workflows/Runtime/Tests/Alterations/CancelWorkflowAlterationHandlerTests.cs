using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class CancelWorkflowAlterationHandlerTests
{
    [Fact]
    public async Task PreflightAsync_StagesOnlyLiveWorkflow_AndPreservesTerminalNoOp()
    {
        var handler = new CancelWorkflowAlterationHandler(new WorkflowCancellationPlanner(
            new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore()),
            TimeProvider.System));
        var staging = new Staging();

        var active = await handler.PreflightAsync(Context(WorkflowExecutionStatus.Running, staging));

        Assert.True(active.IsAccepted);
        var change = Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges));
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(Workflow(WorkflowExecutionStatus.Running));
        change.Apply(builder);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, builder.WorkflowExecution.Status);

        var terminalStaging = new Staging();
        var terminal = await handler.PreflightAsync(Context(WorkflowExecutionStatus.Completed, terminalStaging));
        Assert.True(terminal.IsAccepted);
        Assert.Empty(terminalStaging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsPayload()
    {
        var handler = new CancelWorkflowAlterationHandler(new WorkflowCancellationPlanner(
            new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore()),
            TimeProvider.System));
        var context = Context(WorkflowExecutionStatus.Running, new Staging(), JsonSerializer.SerializeToElement(new { unexpected = true }));

        var result = await handler.PreflightAsync(context);

        Assert.False(result.IsAccepted);
        Assert.Equal("InvalidCancelPayload", result.Failure!.Code);
    }

    [Fact]
    public async Task CancellationPlanner_CapturesRootActivityAndDerivedExecutionScopes()
    {
        var cleanup = new CapturingCleanupStore();
        var planner = new WorkflowCancellationPlanner(
            new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore()),
            TimeProvider.System,
            cleanup);
        var activity = Activity("activity", "derived-scope");

        var plan = await planner.PlanAsync(
            Workflow(WorkflowExecutionStatus.Running),
            [activity],
            "checkpoint",
            new Dictionary<string, string>());

        Assert.NotNull(plan);
        Assert.Equal("workflow", cleanup.WorkflowExecutionId);
        Assert.Equal("workflow", cleanup.ExecutionScopeId);
        Assert.Equal(
            new[] { "activity", "derived-scope", "workflow" },
            cleanup.CapturedIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CancellationPlanner_ChangesAndCleansOnlyLiveActivityScopes()
    {
        var cleanup = new CapturingCleanupStore();
        var planner = new WorkflowCancellationPlanner(
            new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore()),
            TimeProvider.System,
            cleanup);
        var active = Activity("active", "active-scope");
        var completed = Activity("completed", "completed-scope") with { Status = ActivityExecutionStatus.Completed };
        var faulted = Activity("faulted", "faulted-scope") with { Status = ActivityExecutionStatus.Faulted };

        var plan = await planner.PlanAsync(
            Workflow(WorkflowExecutionStatus.Running),
            [active, completed, faulted],
            "checkpoint",
            new Dictionary<string, string>());

        Assert.NotNull(plan);
        Assert.Equal(["active"], plan.ActivityExecutions.Select(change => change.StateId));
        Assert.Equal(["active"], plan.ActivityExecutionInspections.Select(change => change.StateId));
        Assert.Equal(
            new[] { "active", "active-scope", "workflow" },
            cleanup.CapturedIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PreflightAsync_TerminalRaceIsAnAcceptedNoOpWithoutCleanup()
    {
        var cleanup = new CapturingCleanupStore();
        var handler = new CancelWorkflowAlterationHandler(new WorkflowCancellationPlanner(
            new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore()),
            TimeProvider.System,
            cleanup));
        var staging = new Staging();

        var result = await handler.PreflightAsync(Context(WorkflowExecutionStatus.Completed, staging));

        Assert.True(result.IsAccepted);
        Assert.Empty(staging.StagedChanges);
        Assert.Null(cleanup.WorkflowExecutionId);
    }

    private static WorkflowAlterationPreflightContext Context(WorkflowExecutionStatus status, Staging staging, JsonElement? payload = null) =>
        new(Job(), new WorkflowAlterationEnvelope("CancelWorkflow", 1, payload ?? JsonSerializer.SerializeToElement(new { })), 0,
            new WorkflowAlterationProjectedState([Workflow(status), new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([])]), staging);

    private static WorkflowAlterationJobState Job() => new("job", "plan", "workflow", "tenant", 0, WorkflowAlterationJobStatus.Running,
        new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0);

    private static WorkflowExecutionState Workflow(WorkflowExecutionStatus status) => new("workflow", new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"), status,
        null, DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch, null, null, null, "tenant", new Dictionary<string, string>());

    private static ActivityExecutionState Activity(string activityExecutionId, string executionScopeId) => new(
        new ActivityExecution(activityExecutionId, "workflow", "node", "node", "test", "1"),
        ActivityExecutionStatus.Waiting,
        null,
        0,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        null,
        null,
        null,
        null,
        null,
        ActivitySchedulingProvenance.From("workflow", null, null, null, null, null, executionScopeId, null),
        null,
        [],
        [],
        0,
        0,
        new Dictionary<string, string>())
    {
        ExecutionScopeId = executionScopeId
    };

    private sealed class CapturingCleanupStore : IActivityScopeCleanupStore
    {
        public string? WorkflowExecutionId { get; private set; }
        public string? ExecutionScopeId { get; private set; }
        public IReadOnlySet<string> CapturedIds { get; private set; } = new HashSet<string>();

        public ValueTask<ActivityScopeCleanupRequest> CaptureAsync(
            string workflowExecutionId,
            string executionScopeId,
            IReadOnlySet<string> activityExecutionIds,
            CancellationToken cancellationToken = default)
        {
            WorkflowExecutionId = workflowExecutionId;
            ExecutionScopeId = executionScopeId;
            CapturedIds = activityExecutionIds;
            return ValueTask.FromResult(new ActivityScopeCleanupRequest(
                workflowExecutionId,
                executionScopeId,
                activityExecutionIds,
                [],
                [],
                []));
        }

        public ValueTask ApplyAsync(ActivityScopeCleanupRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class Staging : IWorkflowAlterationStagingWorkspace
    {
        public List<IWorkflowAlterationStagedChange> StagedChanges { get; } = [];
        IReadOnlyList<IWorkflowAlterationStagedChange> IWorkflowAlterationStagingWorkspace.StagedChanges => StagedChanges;
        public void Stage(IWorkflowAlterationStagedChange change) => StagedChanges.Add(change);
    }
}
