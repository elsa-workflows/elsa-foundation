using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
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
        await using var harness = await CreateHarnessAsync(NewVariableWriteNode(intrinsicKind));

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

    [Fact]
    public async Task Set_intrinsic_evaluates_only_declared_portable_expression_parameters()
    {
        var evaluator = new RecordingPortableEvaluator();
        await using var harness = await CreateHarnessAsync(
            NewPortableExpressionVariableWriteNode(),
            portableExpressionEvaluator: evaluator);

        await harness.Handler.HandleAsync(harness.WorkItem);

        var persistedWorkflow = await harness.WorkflowStore.FindAsync("wfexec-1");
        Assert.Equal("initial:updated", persistedWorkflow!.RootVariableFrame!.Values["greeting"].InlineValue!.Value.GetString());
        Assert.Equal(ExpressionCapabilityProfiles.BindingPureV1, evaluator.Request!.Capabilities.Profile);
        Assert.Equal(["current", "suffix"], evaluator.Request.ParameterValues.Keys);
        Assert.Equal("initial", evaluator.Request.ParameterValues["current"].GetString());
        Assert.Equal("updated", evaluator.Request.ParameterValues["suffix"].GetString());
    }

    [Theory]
    [InlineData(WorkflowIntrinsicKind.Return, "Done", "returned")]
    [InlineData(WorkflowIntrinsicKind.Control, "Approved", null)]
    public async Task Result_and_control_intrinsics_commit_before_their_selected_continuation(
        WorkflowIntrinsicKind intrinsicKind,
        string expectedOutcome,
        string? expectedResult)
    {
        await using var harness = await CreateHarnessAsync(NewTerminalNode(intrinsicKind));

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

    [Theory]
    [InlineData(WorkflowIntrinsicKind.SetCorrelationId, "order-42")]
    [InlineData(WorkflowIntrinsicKind.SetInstanceName, "Order 42")]
    public async Task Identity_intrinsic_commits_authoritative_state_and_cross_branch_projection_before_continuation(
        WorkflowIntrinsicKind intrinsicKind,
        string assignedValue)
    {
        await using var harness = await CreateHarnessAsync(NewEffectNode(intrinsicKind, assignedValue));

        await harness.Handler.HandleAsync(harness.WorkItem);

        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.IntrinsicCompleted, commit.Checkpoint.Name);
        Assert.Single(commit.PostCommitIntents);
        var workflow = await harness.WorkflowStore.FindAsync("wfexec-1");
        if (intrinsicKind == WorkflowIntrinsicKind.SetCorrelationId)
            Assert.Equal(assignedValue, workflow!.CorrelationId);
        else
            Assert.Equal(assignedValue, workflow!.SystemMetadata[RuntimeMetadataKeys.InstanceName]);
        var identity = Assert.Single(commit.StateChanges.DurableValues).State;
        Assert.Equal(assignedValue, identity.InlineValue!.Value.GetString());
        Assert.Equal(
            intrinsicKind == WorkflowIntrinsicKind.SetCorrelationId
                ? RuntimeWorkflowStateSeed.IdentityCorrelationIdName
                : RuntimeWorkflowStateSeed.IdentityInstanceNameName,
            identity.Metadata[RuntimeMetadataKeys.IdentityName]);
    }

    [Fact]
    public async Task Set_output_intrinsic_commits_typed_named_value_before_continuation()
    {
        await using var harness = await CreateHarnessAsync(NewEffectNode(WorkflowIntrinsicKind.SetOutput, "accepted"));

        await harness.Handler.HandleAsync(harness.WorkItem);

        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        var output = Assert.Single(commit.StateChanges.DurableValues).State;
        Assert.Equal("output:result", output.ValueId);
        Assert.Equal("result", output.Metadata[RuntimeMetadataKeys.OutputName]);
        Assert.Equal("accepted", output.InlineValue!.Value.GetString());
        Assert.Equal("String", output.Type.Id);
        Assert.Single(commit.PostCommitIntents);
    }

    [Fact]
    public async Task Finish_intrinsic_commits_terminal_workflow_checkpoint_and_schedules_no_continuation()
    {
        await using var harness = await CreateHarnessAsync(NewEffectNode(WorkflowIntrinsicKind.Finish, "Aborted"));

        await harness.Handler.HandleAsync(harness.WorkItem);

        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, commit.Checkpoint.Name);
        Assert.Empty(commit.PostCommitIntents);
        Assert.Equal(WorkflowExecutionStatus.Completed, commit.StateChanges.WorkflowExecution!.State.Status);
        Assert.Equal(Now, commit.StateChanges.WorkflowExecution.State.CompletedAt);
        var completed = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Equal("Aborted", completed.Completion!.OutcomeKey);
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
            new Dictionary<string, string>(),
            intrinsicKind: intrinsicKind,
            intrinsicVariable: new RuntimeVariableReference("greeting", VariableReference.WorkflowScopeId));
    }

    private static ExecutableNode NewPortableExpressionVariableWriteNode()
    {
        using var descriptor = JsonDocument.Parse("{}");
        var expression = new RuntimeExpressionBinding(
            "TestPortable",
            "args.current + ':' + args.suffix",
            new RuntimeValueTypeDescriptor("alias", "String", null),
            parameters: new Dictionary<string, ExpressionParameterBinding>
            {
                ["current"] = new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, "greeting"),
                ["suffix"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement("updated"))
            },
            options: JsonSerializer.SerializeToElement(new { }),
            capabilityProfile: ExpressionCapabilityProfiles.BindingPureV1);
        return new ExecutableNode(
            "node-set-expression",
            "authored-set-expression",
            "elsa.intrinsic.set",
            "1.0.0",
            "intrinsic",
            descriptor.RootElement,
            new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Value] = new(
                    WorkflowIntrinsicInputKeys.Value,
                    StringType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Expression,
                    expression: expression)
            },
            new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
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
            new Dictionary<string, string>(),
            intrinsicKind: intrinsicKind);
    }

    private static ExecutableNode NewEffectNode(WorkflowIntrinsicKind intrinsicKind, string value)
    {
        using var descriptor = JsonDocument.Parse("{}");
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        if (intrinsicKind == WorkflowIntrinsicKind.Finish)
            bindings[WorkflowIntrinsicInputKeys.Outcome] = LiteralBinding(WorkflowIntrinsicInputKeys.Outcome, value);
        else
        {
            if (intrinsicKind == WorkflowIntrinsicKind.SetOutput)
                bindings[WorkflowIntrinsicInputKeys.Name] = LiteralBinding(WorkflowIntrinsicInputKeys.Name, "result");
            bindings[WorkflowIntrinsicInputKeys.Value] = LiteralBinding(WorkflowIntrinsicInputKeys.Value, value);
        }

        return new ExecutableNode(
            $"node-{intrinsicKind.ToString().ToLowerInvariant()}",
            $"authored-{intrinsicKind.ToString().ToLowerInvariant()}",
            $"elsa.intrinsic.{intrinsicKind.ToString().ToLowerInvariant()}",
            "1.0.0",
            "intrinsic",
            descriptor.RootElement,
            bindings,
            new Dictionary<string, string>(),
            intrinsicKind: intrinsicKind);
    }

    private static RuntimeInputBinding LiteralBinding(string inputName, string value) => new(
        inputName,
        StringType,
        ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Literal,
        literal: Envelope(value));

    private static async Task<Harness> CreateHarnessAsync(
        ExecutableNode node,
        IPortableExpressionEvaluator? portableExpressionEvaluator = null)
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
            new RuntimeActivityExecutionInspectionAccumulator(inspectionStore),
            new FixedTimeProvider(Now),
            portableExpressionEvaluator: portableExpressionEvaluator);
        var services = new ServiceCollection();
        services.AddScoped(_ => executor);
        var serviceProvider = services.BuildServiceProvider();
        var handler = new WorkflowStartActivitySchedulerWorkHandler(
            executableStore,
            activityStore,
            queue,
            committer,
            new RuntimeActivityExecutionInspectionAccumulator(inspectionStore),
            new FixedTimeProvider(Now),
            serviceProvider.GetRequiredService<IServiceScopeFactory>());
        return new Harness(
            handler,
            NewStartWorkItem(identity, node),
            commitStore,
            workflowStore,
            serviceProvider);
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

    private sealed class RecordingPortableEvaluator : IPortableExpressionEvaluator
    {
        public ExpressionEvaluationRequest? Request { get; private set; }

        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
        {
            Request = request;
            var value = $"{request.ParameterValues["current"].GetString()}:{request.ParameterValues["suffix"].GetString()}";
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(value));
        }
    }

    private sealed record Harness(
        WorkflowStartActivitySchedulerWorkHandler Handler,
        RuntimeSchedulerWorkItem WorkItem,
        InMemoryRuntimeCheckpointCommitStore CommitStore,
        InMemoryWorkflowExecutionStateStore WorkflowStore,
        ServiceProvider ServiceProvider) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ServiceProvider.DisposeAsync();
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
