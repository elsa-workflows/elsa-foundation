using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ActivityInputSnapshotCheckpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly ValueTypeDescriptor StringType = new("String");
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryWorkflowExecutionStateStore _workflowStateStore = new();

    [Fact]
    public async Task ActivityStarted_checkpoint_commits_complete_input_snapshot_and_first_attempt_before_invoke_intent()
    {
        var executable = NewExecutable(
            ("message", DurableLiteral("message", "hello")),
            ("recipient", DurableLiteral("recipient", "world")));
        await _executableStore.SaveAsync(executable);
        await SaveWorkflowStateAsync(executable.Identity);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var commitStore = NewCommitStore();
        var handler = NewHandler(commitStore);

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var commit = Assert.Single(commitStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.ActivityStarted, commit.Checkpoint.Name);
        var committedState = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Equal(ActivityExecutionStatus.Running, committedState.Status);
        Assert.Equal("hello", committedState.InputSnapshot!.Values["message"].InlineValue!.Value.GetString());
        Assert.Equal("world", committedState.InputSnapshot.Values["recipient"].InlineValue!.Value.GetString());
        Assert.Equal(executable.RootActivity.ActivityContract!.SchemaFingerprint, committedState.InputSnapshot.ContractFingerprint);
        var attempt = Assert.Single(committedState.Attempts!);
        Assert.Equal(ActivityAttemptReason.Initial, attempt.Reason);
        Assert.Equal("actexec-1", attempt.InvocationId);
        Assert.Single(commit.PostCommitIntents);

        var stored = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(stored!.InputSnapshot);
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task Input_materialization_failure_commits_neither_partial_snapshot_nor_running_transition()
    {
        var executable = NewExecutable(
            ("message", DurableLiteral("message", "hello")),
            ("recipient", TransientLiteral("recipient", "unsafe")));
        await _executableStore.SaveAsync(executable);
        await SaveWorkflowStateAsync(executable.Identity);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var commitStore = NewCommitStore();
        var handler = NewHandler(commitStore);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(NewStartWorkItem(executable.Identity)).AsTask());

        Assert.Contains("VF-ACT-005", exception.Message, StringComparison.Ordinal);
        var stored = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Scheduled, stored!.Status);
        Assert.Null(stored.InputSnapshot);
        Assert.Null(stored.Attempts);
        Assert.Empty(commitStore.ListCommits());
        Assert.Empty(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    private WorkflowStartActivitySchedulerWorkHandler NewHandler(InMemoryRuntimeCheckpointCommitStore commitStore) =>
        new(
            _executableStore,
            _activityStateStore,
            _schedulerWorkQueue,
            new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), commitStore),
            new RuntimeActivityExecutionInspectionAccumulator(_inspectionStore),
            new FixedTimeProvider(Now),
            new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver()),
            _durableValueStateStore,
            workflowExecutionStateStore: _workflowStateStore);

    private ValueTask<WorkflowExecutionState> SaveWorkflowStateAsync(WorkflowExecutableIdentity identity)
    {
        var root = new VariableFrameFactory().CreateRoot(
            "wfexec-1",
            Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId,
            new Dictionary<string, ValueEnvelope>());
        return _workflowStateStore.SaveAsync(new WorkflowExecutionState(
            "wfexec-1",
            identity,
            WorkflowExecutionStatus.Running,
            null,
            Now,
            Now,
            Now,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>())
        { RootVariableFrame = root });
    }

    private InMemoryRuntimeCheckpointCommitStore NewCommitStore() =>
        new(activityExecutionStateStore: _activityStateStore, activityExecutionInspectionWriter: _inspectionStore);

    private static WorkflowExecutable NewExecutable(params (string Key, RuntimeInputBinding Binding)[] inputs)
    {
        using var descriptor = JsonDocument.Parse("""{"type":"test"}""");
        var contractInputs = inputs.Select(input => new ActivityInputContract(
            input.Key,
            input.Key,
            StringType,
            isRequired: true,
            hasDefault: false,
            defaultValue: null,
            ActivityValuePolicy.Default));
        var contract = new ActivityContract(
            "test/activity",
            "1.0.0",
            "test",
            descriptor.RootElement,
            contractInputs,
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/activity"));
        var node = new ExecutableNode(
            "node-start",
            "authored-node-start",
            "test/activity",
            "1.0.0",
            "test",
            descriptor.RootElement,
            inputs.ToDictionary(input => input.Key, input => input.Binding, StringComparer.Ordinal),
            new Dictionary<string, string>(),
            activityContract: contract);

        return new WorkflowExecutable(
            NewIdentity(),
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now,
            new Dictionary<string, string>());
    }

    private static RuntimeInputBinding DurableLiteral(string key, string value) =>
        new(
            key,
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private static RuntimeInputBinding TransientLiteral(string key, string value) =>
        new(
            key,
            StringType,
            ValueProtectionPolicy.Transient,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.Transient));

    private static ActivityExecutionState NewScheduledState() =>
        new(
            Execution: new ActivityExecution("actexec-1", "wfexec-1", "node-start", "authored-node-start", "test/activity", "1.0.0"),
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
            Metadata: new Dictionary<string, string>());

    private static RuntimeSchedulerWorkItem NewStartWorkItem(WorkflowExecutableIdentity identity) =>
        new(
            workItemId: "start-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.StartActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:start:actexec-1",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 20,
            payload: JsonSerializer.SerializeToElement(new RuntimeStartActivityCommandPayload(
                identity,
                "node-start",
                "actexec-1",
                RuntimeStartActivityCommandPayload.ScheduledActivityReason)),
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
