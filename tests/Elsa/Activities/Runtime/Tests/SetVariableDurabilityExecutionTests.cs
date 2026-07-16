using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class SetVariableDurabilityExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly ValueTypeDescriptor StringType = new("String");

    [Fact]
    public async Task Intrinsic_set_commits_frame_change_before_continuation_and_recovery_does_not_reapply_it()
    {
        var identity = new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
        var node = NewSetNode();
        var executable = new WorkflowExecutable(identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>());
        var executableStore = new InMemoryWorkflowExecutableStore();
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var activityStore = new InMemoryActivityExecutionStateStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var inspectionStore = new InMemoryActivityExecutionInspectionStore();
        await executableStore.SaveAsync(executable);
        await workflowStore.SaveAsync(NewWorkflowState(identity));
        await activityStore.SaveAsync(NewScheduledState());

        var commitStore = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: workflowStore,
            activityExecutionStateStore: activityStore,
            activityExecutionInspectionWriter: inspectionStore,
            rootWriteLeaseManager: PassThroughRootWriteLeaseManager.Instance);
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), commitStore);
        var executor = new WorkflowIntrinsicExecutor(
            workflowStore,
            activityStore,
            new RuntimeInputBindingResolver(),
            new InMemoryDurableValueStateStore(),
            new InMemoryRuntimeActivityOutputRegister(),
            new RuntimeActivityExecutionInspectionAccumulator(inspectionStore),
            new FixedTimeProvider(Now));
        var handler = new WorkflowStartActivitySchedulerWorkHandler(
            executableStore,
            activityStore,
            queue,
            committer,
            new RuntimeActivityExecutionInspectionAccumulator(inspectionStore),
            new FixedTimeProvider(Now),
            intrinsicExecutor: executor);
        var workItem = NewStartWorkItem(identity);

        await handler.HandleAsync(workItem);

        var commit = Assert.Single(commitStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.IntrinsicCompleted, commit.Checkpoint.Name);
        Assert.NotNull(commit.StateChanges.WorkflowExecution);
        var committedActivity = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Equal(ActivityExecutionStatus.Completed, committedActivity.Status);
        Assert.Empty(committedActivity.Attempts!);
        Assert.Null(committedActivity.InputSnapshot);
        var completionIntent = Assert.Single(commit.PostCommitIntents);
        var completionWorkItem = completionIntent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>();
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, completionWorkItem!.CommandKind);

        var persistedWorkflow = await workflowStore.FindAsync("wfexec-1");
        Assert.Equal(1, persistedWorkflow!.RootVariableFrame!.Revision);
        Assert.Equal("updated", persistedWorkflow.RootVariableFrame.Values["greeting"].InlineValue!.Value.GetString());

        await handler.HandleAsync(workItem);

        Assert.Single(commitStore.ListCommits());
        persistedWorkflow = await workflowStore.FindAsync("wfexec-1");
        Assert.Equal(1, persistedWorkflow!.RootVariableFrame!.Revision);
    }

    private static ExecutableNode NewSetNode()
    {
        using var descriptor = JsonDocument.Parse("{}" );
        return new ExecutableNode(
            "node-set",
            "authored-set",
            "elsa.intrinsic.set",
            "1.0.0",
            "intrinsic",
            descriptor.RootElement,
            new Dictionary<string, RuntimeInputBinding>
            {
                ["value"] = new(
                    "value",
                    StringType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: Envelope("updated"))
            },
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference("greeting", VariableReference.WorkflowScopeId));
    }

    private static WorkflowExecutionState NewWorkflowState(WorkflowExecutableIdentity identity) =>
        new(
            "wfexec-1",
            identity,
            WorkflowExecutionStatus.Running,
            null,
            Now.AddMinutes(-1),
            Now.AddMinutes(-1),
            Now.AddMinutes(-1),
            null,
            null,
            null,
            null,
            new Dictionary<string, string>())
        {
            RootVariableFrame = new VariableFrameFactory().CreateRoot(
                "wfexec-1",
                VariableReference.WorkflowScopeId,
                new Dictionary<string, ValueEnvelope> { ["greeting"] = Envelope("initial") })
        };

    private static ActivityExecutionState NewScheduledState() =>
        new(
            Execution: new ActivityExecution("actexec-set", "wfexec-1", "node-set", "authored-set", "elsa.intrinsic.set", "1.0.0"),
            Status: ActivityExecutionStatus.Scheduled,
            SubStatus: null,
            ScheduledAt: Now.AddSeconds(-1),
            StartedAt: null,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>())
        {
            Attempts = []
        };

    private static RuntimeSchedulerWorkItem NewStartWorkItem(WorkflowExecutableIdentity identity) =>
        new(
            "start-set",
            "wfexec-1",
            "command-set",
            WorkflowExecutionCommandKind.StartActivity,
            "envelope-1",
            "wfexec-1:start:actexec-set",
            Now,
            Now,
            1,
            JsonSerializer.SerializeToElement(new RuntimeStartActivityCommandPayload(
                identity,
                "node-set",
                "actexec-set",
                RuntimeStartActivityCommandPayload.ScheduledActivityReason)),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

    private static ValueEnvelope Envelope(string value) =>
        ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public static PassThroughRootWriteLeaseManager Instance { get; } = new();

        public async ValueTask ExecuteAsync(
            string artifactId,
            string leaseId,
            Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) =>
            await write(cancellationToken);
    }
}
