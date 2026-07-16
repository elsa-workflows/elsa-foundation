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

    [Theory]
    [InlineData(WorkflowIntrinsicKind.Set)]
    [InlineData(WorkflowIntrinsicKind.Merge)]
    [InlineData(WorkflowIntrinsicKind.Reduce)]
    public async Task Variable_write_intrinsic_commits_frame_change_before_continuation_and_recovery_does_not_reapply_it(
        WorkflowIntrinsicKind intrinsicKind)
    {
        var harness = await CreateHarnessAsync(NewVariableWriteNode(intrinsicKind));

        await harness.Handler.HandleAsync(harness.WorkItem);

        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.IntrinsicCompleted, commit.Checkpoint.Name);
        Assert.Equal(intrinsicKind.ToString(), commit.Metadata[RuntimeMetadataKeys.CheckpointReason]);
        Assert.NotNull(commit.StateChanges.WorkflowExecution);
        var committedActivity = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Equal(ActivityExecutionStatus.Completed, committedActivity.Status);
        Assert.Empty(committedActivity.Attempts!);
        Assert.Null(committedActivity.InputSnapshot);
        var completionIntent = Assert.Single(commit.PostCommitIntents);
        var completionWorkItem = completionIntent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>();
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, completionWorkItem!.CommandKind);

        var persistedWorkflow = await harness.WorkflowStore.FindAsync("wfexec-1");
        Assert.Equal(1, persistedWorkflow!.RootVariableFrame!.Revision);
        Assert.Equal("updated", persistedWorkflow.RootVariableFrame.Values["greeting"].InlineValue!.Value.GetString());

        await harness.Handler.HandleAsync(harness.WorkItem);

        Assert.Single(harness.CommitStore.ListCommits());
        persistedWorkflow = await harness.WorkflowStore.FindAsync("wfexec-1");
        Assert.Equal(1, persistedWorkflow!.RootVariableFrame!.Revision);
    }

    [Theory]
    [InlineData(WorkflowIntrinsicKind.Return, "Done", "returned")]
    [InlineData(WorkflowIntrinsicKind.Control, "Approved", null)]
    public async Task Result_and_control_intrinsics_commit_before_their_selected_continuation(
        WorkflowIntrinsicKind intrinsicKind,
        string expectedOutcome,
        string? expectedResult)
    {
        var harness = await CreateHarnessAsync(NewTerminalNode(intrinsicKind));

        await harness.Handler.HandleAsync(harness.WorkItem);

        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        var completed = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Equal(ActivityExecutionStatus.Completed, completed.Status);
        Assert.Equal(expectedOutcome, completed.Completion!.OutcomeKey);
        if (expectedResult is null)
            Assert.Equal("Elsa.Activities.Runtime.Core.Models.ActivityUnit", completed.Completion.Result.Type.Alias);
        else
            Assert.Equal(expectedResult, completed.Completion.Result.InlineValue!.Value.GetString());

        var workflow = await harness.WorkflowStore.FindAsync("wfexec-1");
        Assert.Equal(0, workflow!.RootVariableFrame!.Revision);
        var intent = Assert.Single(commit.PostCommitIntents);
        var workItem = intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>();
        var payload = workItem!.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>();
        Assert.Equal([expectedOutcome], payload!.OutcomeNames);
    }

    private static ExecutableNode NewVariableWriteNode(WorkflowIntrinsicKind intrinsicKind)
    {
        using var descriptor = JsonDocument.Parse("{}" );
        return new ExecutableNode(
            "node-set",
            "authored-set",
            $"elsa.intrinsic.{intrinsicKind.ToString().ToLowerInvariant()}",
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
            intrinsicKind: intrinsicKind,
            intrinsicVariable: new RuntimeVariableReference("greeting", VariableReference.WorkflowScopeId));
    }

    private static ExecutableNode NewTerminalNode(WorkflowIntrinsicKind intrinsicKind)
    {
        using var descriptor = JsonDocument.Parse("{}");
        var control = intrinsicKind == WorkflowIntrinsicKind.Control;
        var inputKey = control ? WorkflowIntrinsicInputKeys.Outcome : WorkflowIntrinsicInputKeys.Value;
        var value = control ? "Approved" : "returned";
        return new ExecutableNode(
            $"node-{intrinsicKind.ToString().ToLowerInvariant()}",
            $"authored-{intrinsicKind.ToString().ToLowerInvariant()}",
            $"elsa.intrinsic.{intrinsicKind.ToString().ToLowerInvariant()}",
            "1.0.0",
            "intrinsic",
            descriptor.RootElement,
            new Dictionary<string, RuntimeInputBinding>
            {
                [inputKey] = new(
                    inputKey,
                    StringType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: Envelope(value))
            },
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            intrinsicKind: intrinsicKind);
    }

    private static async Task<Harness> CreateHarnessAsync(ExecutableNode node)
    {
        var identity = new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
        var executable = new WorkflowExecutable(identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>());
        var executableStore = new InMemoryWorkflowExecutableStore();
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var activityStore = new InMemoryActivityExecutionStateStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var inspectionStore = new InMemoryActivityExecutionInspectionStore();
        await executableStore.SaveAsync(executable);
        await workflowStore.SaveAsync(NewWorkflowState(identity));
        await activityStore.SaveAsync(NewScheduledState(node));

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
        return new Harness(
            handler,
            NewStartWorkItem(identity, node),
            commitStore,
            workflowStore);
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

    private static ActivityExecutionState NewScheduledState(ExecutableNode node) =>
        new(
            Execution: new ActivityExecution("actexec-set", "wfexec-1", node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, "1.0.0"),
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

    private static RuntimeSchedulerWorkItem NewStartWorkItem(WorkflowExecutableIdentity identity, ExecutableNode node) =>
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
                node.ExecutableNodeId,
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

    private sealed record Harness(
        WorkflowStartActivitySchedulerWorkHandler Handler,
        RuntimeSchedulerWorkItem WorkItem,
        InMemoryRuntimeCheckpointCommitStore CommitStore,
        InMemoryWorkflowExecutionStateStore WorkflowStore);

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
