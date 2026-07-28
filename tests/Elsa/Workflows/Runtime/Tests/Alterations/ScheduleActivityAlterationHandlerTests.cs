using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class ScheduleActivityAlterationHandlerTests
{
    [Fact]
    public void PolicyRegistry_RejectsDuplicateExactPolicyIdentity()
    {
        Assert.Throws<ArgumentException>(() => new OperatorActivitySchedulingPolicyRegistry([new AllowPolicy(), new AllowPolicy()]));
    }

    [Fact]
    public async Task PreflightAsync_StagesDurableScheduleContinuationOnlyForCompiledDirectCapability()
    {
        var executable = Executable(withCapability: true);
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(executable);
        var handler = new ScheduleActivityAlterationHandler(
            store,
            new OperatorActivitySchedulingPolicyRegistry([new AllowPolicy()]),
            TimeProvider.System);
        var staging = new Staging();

        var result = await handler.PreflightAsync(Context(executable.Identity, staging));

        Assert.True(result.IsAccepted);
        var change = Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges));
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(Workflow(executable.Identity));
        change.Apply(builder);
        var intent = Assert.Single(builder.PostCommitIntents);
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, intent.Kind);
        Assert.Equal("workflow", intent.WorkflowExecutionId);
    }

    [Fact]
    public async Task PreflightAsync_RejectsRelationWithoutCompiledCapability()
    {
        var executable = Executable(withCapability: false);
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(executable);
        var handler = new ScheduleActivityAlterationHandler(store, new OperatorActivitySchedulingPolicyRegistry([]), TimeProvider.System);
        var staging = new Staging();

        var result = await handler.PreflightAsync(Context(executable.Identity, staging));

        Assert.False(result.IsAccepted);
        Assert.Equal("OperatorSchedulingNotSupported", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RequiresAnExactRuntimePolicyVersion()
    {
        var executable = Executable(withCapability: true);
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(executable);
        var handler = new ScheduleActivityAlterationHandler(store, new OperatorActivitySchedulingPolicyRegistry([new VersionTwoPolicy()]), TimeProvider.System);
        var staging = new Staging();

        var result = await handler.PreflightAsync(Context(executable.Identity, staging));

        Assert.False(result.IsAccepted);
        Assert.Equal("OperatorSchedulingPolicyUnavailable", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_PropagatesActivityOwnedPolicyRejection()
    {
        var executable = Executable(withCapability: true);
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(executable);
        var handler = new ScheduleActivityAlterationHandler(store, new OperatorActivitySchedulingPolicyRegistry([new DenyPolicy()]), TimeProvider.System);
        var staging = new Staging();

        var result = await handler.PreflightAsync(Context(executable.Identity, staging));

        Assert.False(result.IsAccepted);
        Assert.Equal("ParentNotEligible", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsDuplicateScheduleSlotWithinOneJob()
    {
        var executable = Executable(withCapability: true);
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(executable);
        var handler = new ScheduleActivityAlterationHandler(store, new OperatorActivitySchedulingPolicyRegistry([new AllowPolicy()]), TimeProvider.System);
        var parent = Parent();
        var projected = new WorkflowAlterationProjectedState([Workflow(executable.Identity), new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([parent])]);
        var envelope = new WorkflowAlterationEnvelope("ScheduleActivity", 1, JsonSerializer.SerializeToElement(new { nodeId = "child", parentActivityExecutionId = parent.Execution.ActivityExecutionId }));
        var staging = new Staging();

        var first = await handler.PreflightAsync(new WorkflowAlterationPreflightContext(Job(), envelope, 0, projected, staging));
        var duplicate = await handler.PreflightAsync(new WorkflowAlterationPreflightContext(Job(), envelope, 1, projected, staging));

        Assert.True(first.IsAccepted);
        Assert.False(duplicate.IsAccepted);
        Assert.Equal("ScheduleActivitySlotConflict", duplicate.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsParentChangedAfterCapture()
    {
        var executable = Executable(withCapability: true);
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(executable);
        var parent = Parent();
        var job = new WorkflowAlterationJobState("job", "plan", "workflow", "tenant", 0, WorkflowAlterationJobStatus.Running,
            new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0,
            new WorkflowAlterationCapturedConcurrency(null, null, executable.Identity, new Dictionary<string, string> { [parent.Execution.ActivityExecutionId] = "stale" }));
        var context = new WorkflowAlterationPreflightContext(
            job,
            new WorkflowAlterationEnvelope("ScheduleActivity", 1, JsonSerializer.SerializeToElement(new { nodeId = "child", parentActivityExecutionId = parent.Execution.ActivityExecutionId })),
            0,
            new WorkflowAlterationProjectedState([Workflow(executable.Identity), new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([parent])]),
            new Staging());

        var result = await new ScheduleActivityAlterationHandler(store, new OperatorActivitySchedulingPolicyRegistry([new AllowPolicy()]), TimeProvider.System).PreflightAsync(context);

        Assert.False(result.IsAccepted);
        Assert.Equal("ActivityStateRevisionConflict", result.Failure!.Code);
    }

    private static WorkflowAlterationPreflightContext Context(WorkflowExecutableIdentity identity, Staging staging) => new(
        Job(),
        new WorkflowAlterationEnvelope("ScheduleActivity", 1, JsonSerializer.SerializeToElement(new { nodeId = "child", parentActivityExecutionId = "parent-exec" })),
        0,
        new WorkflowAlterationProjectedState([Workflow(identity), new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([Parent()])]),
        staging);

    private static WorkflowAlterationJobState Job() => new("job", "plan", "workflow", "tenant", 0, WorkflowAlterationJobStatus.Running,
        new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0);

    private static WorkflowExecutable Executable(bool withCapability)
    {
        var child = new ExecutableNode("child", "child", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var capability = withCapability ? new OperatorActivitySchedulingCapability("test.policy", 1, JsonSerializer.SerializeToElement(new { })) : null;
        var parent = new ExecutableNode("parent", "parent", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), [new ExecutableChildSlot("children", [child], capability)]);
        var identity = new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash");
        return new WorkflowExecutable(identity, parent, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutionState Workflow(WorkflowExecutableIdentity identity) => new("workflow", identity, WorkflowExecutionStatus.Running, null, DateTimeOffset.UnixEpoch, null, null, null, null, null, "tenant", new Dictionary<string, string>());

    private static ActivityExecutionState Parent() => new(
        new ActivityExecution("parent-exec", "workflow", "parent", "parent", "test", "1"),
        ActivityExecutionStatus.Running, null, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, null, null, null,
        ActivitySchedulingProvenance.From("workflow", null, null, null, null, null, "parent-exec", "test"), null, [], [], 0, 0, new Dictionary<string, string>())
    {
        ExecutionScopeId = "parent-exec"
    };

    private class AllowPolicy : IOperatorActivitySchedulingPolicy
    {
        public string PolicyKey => "test.policy";
        public virtual int SchemaVersion => 1;
        public OperatorActivitySchedulingPolicyDecision Evaluate(OperatorActivitySchedulingPolicyContext context) =>
            OperatorActivitySchedulingPolicyDecision.Accepted(ActivitySchedulingProvenance.From(
                context.Workflow.WorkflowExecutionId,
                context.Parent.Execution.ActivityExecutionId,
                context.Parent.Execution.ActivityExecutionId,
                null,
                null,
                null,
                context.Parent.ExecutionScopeId,
                "test"));
    }

    private sealed class VersionTwoPolicy : AllowPolicy
    {
        public override int SchemaVersion => 2;
    }

    private sealed class DenyPolicy : IOperatorActivitySchedulingPolicy
    {
        public string PolicyKey => "test.policy";
        public int SchemaVersion => 1;
        public OperatorActivitySchedulingPolicyDecision Evaluate(OperatorActivitySchedulingPolicyContext context) =>
            OperatorActivitySchedulingPolicyDecision.Rejected("ParentNotEligible", "The parent is not accepting operator scheduling.");
    }

    private sealed class Staging : IWorkflowAlterationStagingWorkspace
    {
        public List<IWorkflowAlterationStagedChange> StagedChanges { get; } = [];
        IReadOnlyList<IWorkflowAlterationStagedChange> IWorkflowAlterationStagingWorkspace.StagedChanges => StagedChanges;
        public void Stage(IWorkflowAlterationStagedChange change) => StagedChanges.Add(change);
    }
}
