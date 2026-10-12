using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Elsa.Workflows.Runtime.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeStartActivityStateTests : IDisposable
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryWorkflowExecutionStateStore _workflowStateStore = new();
    private readonly ServiceProvider _serviceProvider = new ServiceCollection()
        .AddScoped<IRuntimeActivityInputMaterializer>(_ => new RuntimeActivityInputMaterializer(
            new SecretReadRefusingResolver(),
            new StringTypeRegistry(),
            new FixedTextEvaluator()))
        .BuildServiceProvider();

    [Fact]
    public async Task HandleAsync_TransitionsScheduledActivityExecutionStateToRunning()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await SaveWorkflowStateAsync(executable.Identity);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var handler = NewHandler();

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.Equal(_now, state.StartedAt);
        Assert.Equal("actexec-1", state.Execution.ActivityExecutionId);
        Assert.Equal("node-start", state.Execution.ExecutableNodeId);
        Assert.Equal(RuntimeStartActivityCommandPayload.ScheduledActivityReason, state.Metadata["runtime.startReason"]);
        Assert.Equal("start-work", state.Metadata["runtime.startSchedulerWorkItemId"]);
        Assert.Empty(await _schedulerWorkQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_CheckpointsRunningStateAndInspectionBeforePostCommitInvokeWork()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await SaveWorkflowStateAsync(executable.Identity);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(null, _activityStateStore, null, null, null, null, null, _inspectionStore);
        var handler = NewCheckpointingHandler(checkpointWriter);

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var write = Assert.Single(checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityStarted, write.Commit.Checkpoint.Name);
        Assert.Equal(RuntimeMetadataKeys.CheckpointRequirementMandatory, write.Commit.Checkpoint.Metadata[RuntimeMetadataKeys.CheckpointRequirement]);
        Assert.Single(write.Commit.StateChanges.ActivityExecutions);
        Assert.Single(write.Commit.StateChanges.ActivityExecutionInspections);
        Assert.Single(write.Commit.PostCommitIntents);

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Running, projection.Status);
        Assert.Equal(_now, projection.StartedAt);

        Assert.Empty(await _schedulerWorkQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var pending = Assert.Single(await checkpointWriter.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, limit: 10, workflowExecutionId: "wfexec-1")));
        var invokeWork = pending.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.InvokeActivity, invokeWork.CommandKind);
    }

    [Fact]
    public async Task HandleAsync_MaterializesASecretReadAsAWithheldReferenceAndPersistsNoValue()
    {
        var reference = new RuntimeSecretReference("payments.api-key", "text");
        var plan = ValueConversionPlan.Identity(StringType, ValueRepresentation.TextValue);
        var executable = NewExecutable(SecretInputContract(), new Dictionary<string, RuntimeInputBinding>
        {
            ["apiKey"] = new(
                "apiKey",
                StringType,
                new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true),
                RuntimeInputBindingSource.SecretRead,
                conversionPlan: plan,
                secret: reference)
        });
        await _executableStore.SaveAsync(executable);
        await SaveWorkflowStateAsync(executable.Identity);
        await _activityStateStore.SaveAsync(NewScheduledState());

        await NewHandler().HandleAsync(NewStartWorkItem(executable.Identity));

        var snapshot = (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!.InputSnapshot!;
        var envelope = snapshot.Values["apiKey"];
        Assert.Equal(ValuePresence.Withheld, envelope.Presence);
        Assert.Null(envelope.InlineValue);
        Assert.Null(envelope.ExternalReference);
        Assert.True(envelope.Policy.IsSensitive);
        Assert.True(envelope.Policy.RequiresEncryption);
        Assert.Equal(WithheldValueKind.SecretReference, envelope.WithheldValue!.Kind);
        Assert.Equal(reference, envelope.WithheldValue.Secret);
        Assert.Equal(plan.Fingerprint, envelope.WithheldValue.ConversionPlan!.Fingerprint);

        var persisted = JsonNode.Parse(JsonSerializer.Serialize(snapshot))!["Values"]!["apiKey"]!.AsObject();
        Assert.Null(persisted["InlineValue"]);
        Assert.Null(persisted["ExternalReference"]);
        Assert.Equal("payments.api-key", persisted["withheld"]!["Secret"]!["Name"]!.GetValue<string>());
    }

    [Fact]
    public async Task HandleAsync_MaterializesASensitiveExpressionInputAsAPresentValueWithItsPolicy()
    {
        // Spec 188, T104: an input the activity declares sensitive, but not a credential, may take an expression. Its
        // effective policy is sensitive and does not require encryption, so its value is present, not withheld.
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: false);
        var executable = NewExecutable(
            InputContract("note", "Note", ActivityValuePolicy.Default with { IsSensitive = true }),
            new Dictionary<string, RuntimeInputBinding>
            {
                ["note"] = new(
                    "note",
                    StringType,
                    policy,
                    RuntimeInputBindingSource.Expression,
                    expression: new RuntimeExpressionBinding("JavaScript", "readNote()", new RuntimeValueTypeDescriptor("alias", "String", null)))
            });
        await _executableStore.SaveAsync(executable);
        await SaveWorkflowStateAsync(executable.Identity);
        await _activityStateStore.SaveAsync(NewScheduledState());

        await NewHandler().HandleAsync(NewStartWorkItem(executable.Identity));

        var envelope = (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!.InputSnapshot!.Values["note"];
        Assert.Equal(ValuePresence.Present, envelope.Presence);
        Assert.Null(envelope.WithheldValue);
        Assert.Equal(FixedTextEvaluator.Text, envelope.InlineValue!.Value.GetString());
        Assert.True(envelope.Policy.IsSensitive);
        Assert.False(envelope.Policy.RequiresEncryption);
    }

    [Fact]
    public async Task HandleAsync_ReenqueuesInvokeActivityWorkForExistingRunningState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewScheduledState() with
        {
            Status = ActivityExecutionStatus.Running,
            StartedAt = _now.AddMinutes(-1),
            InputSnapshot = EmptySnapshot()
        });
        var handler = NewHandler();

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.Equal(_now.AddMinutes(-1), state.StartedAt);
        var invokeWork = Assert.Single(await _schedulerWorkQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.InvokeActivity, invokeWork.CommandKind);
    }

    [Fact]
    public async Task HandleAsync_DoesNotOverwriteOrEnqueueForExistingLaterLifecycleState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewScheduledState() with
        {
            Status = ActivityExecutionStatus.Completed,
            StartedAt = _now.AddMinutes(-1),
            CompletedAt = _now
        });
        var handler = NewHandler();

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Equal(_now.AddMinutes(-1), state.StartedAt);
        Assert.Equal(_now, state.CompletedAt);
        Assert.Empty(await _schedulerWorkQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_IgnoresSourceReferenceWhenCheckingPinnedExecutableSnapshot()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await SaveWorkflowStateAsync(executable.Identity);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var pinned = executable.Identity;
        var handler = NewHandler();

        await handler.HandleAsync(NewStartWorkItem(pinned));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingPayloadBeforeChangingState()
    {
        await _activityStateStore.SaveAsync(NewScheduledState());
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(includePayload: false)).AsTask());

        Assert.Contains("requires a start activity payload", exception.Message);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Scheduled, state.Status);
    }

    [Fact]
    public async Task HandleAsync_RejectsMalformedPayloadBeforeChangingState()
    {
        using var document = JsonDocument.Parse("[]");
        await _activityStateStore.SaveAsync(NewScheduledState());
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(payload: document.RootElement.Clone())).AsTask());

        Assert.Contains("not a valid start activity payload", exception.Message);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Scheduled, state.Status);
    }

    [Fact]
    public async Task HandleAsync_RejectsPinnedExecutableMismatchBeforeChangingState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var pinned = executable.Identity with { ArtifactHash = "sha256:pinned" };
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(pinned)).AsTask());

        Assert.Contains("pinned executable artifact", exception.Message);
        Assert.Contains("definition-1/version-1", exception.Message);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Scheduled, state.Status);
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingActivityExecutionState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(executable.Identity)).AsTask());

        Assert.Contains("missing activity execution", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_RejectsExecutableNodeMismatchBeforeChangingState()
    {
        var executable = NewExecutable();
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewScheduledState());
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(executable.Identity, executableNodeId: "node-other")).AsTask());

        Assert.Contains("belongs to executable node 'node-start'", exception.Message);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Scheduled, state.Status);
    }

    [Fact]
    public void CanHandle_AcceptsOnlyStartActivityWork()
    {
        var handler = NewHandler();

        Assert.True(handler.CanHandle(NewStartWorkItem(NewIdentity())));
        Assert.False(handler.CanHandle(NewStartWorkItem(NewIdentity(), commandKind: WorkflowExecutionCommandKind.ScheduleActivity)));
    }

    private WorkflowStartActivitySchedulerWorkHandler NewHandler() =>
        new(
            _executableStore,
            _activityStateStore,
            _schedulerWorkQueue,
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                new InMemoryRuntimeCheckpointCommitStore(null, _activityStateStore, null, null, null, null, null, _inspectionStore), new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []),
            new RuntimeActivityExecutionInspectionAccumulator(_inspectionStore),
            new FakeTimeProvider(_now),
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            durableValueStateStore: _durableValueStateStore,
            workflowExecutionStateStore: _workflowStateStore);

    private WorkflowStartActivitySchedulerWorkHandler NewCheckpointingHandler(InMemoryRuntimeCheckpointCommitStore checkpointWriter) =>
        new(
            _executableStore,
            _activityStateStore,
            _schedulerWorkQueue,
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                checkpointWriter, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []),
            new RuntimeActivityExecutionInspectionAccumulator(_inspectionStore),
            new FakeTimeProvider(_now),
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _durableValueStateStore,
            workflowExecutionStateStore: _workflowStateStore);

    public void Dispose() => _serviceProvider.Dispose();

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
            _now,
            _now,
            _now,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>())
        { RootVariableFrame = root });
    }

    private RuntimeSchedulerWorkItem NewStartWorkItem(
        WorkflowExecutableIdentity? pinnedExecutable = null,
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.StartActivity,
        string executableNodeId = "node-start",
        JsonElement? payload = null,
        bool includePayload = true)
    {
        var resolvedPayload = includePayload
            ? payload ?? JsonSerializer.SerializeToElement(new RuntimeStartActivityCommandPayload(
                pinnedExecutable ?? NewIdentity(),
                executableNodeId,
                "actexec-1",
                RuntimeStartActivityCommandPayload.ScheduledActivityReason))
            : (JsonElement?)null;

        return new RuntimeSchedulerWorkItem(
            workItemId: "start-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:start:actexec-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 20,
            payload: resolvedPayload,
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static ActivityExecutionState NewScheduledState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-start",
                AuthoredActivityId: "authored-node-start",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Scheduled,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UtcNow,
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
            Metadata: new Dictionary<string, string> { ["runtime.scheduleReason"] = "test" });

    private static readonly ValueTypeDescriptor StringType = new("String");

    private static WorkflowExecutable NewExecutable(
        ActivityContract? contract = null,
        IReadOnlyDictionary<string, RuntimeInputBinding>? inputBindings = null)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var start = NewNode("node-start", document.RootElement, contract, inputBindings);
        var other = NewNode("node-other", document.RootElement);

        return new(
            identity: NewIdentity(),
            rootActivity: WithChildren(start, [other]),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private static ExecutableNode WithChildren(ExecutableNode root, IReadOnlyCollection<ExecutableNode> children) =>
        new(
            executableNodeId: root.ExecutableNodeId,
            authoredActivityId: root.AuthoredActivityId,
            activityType: root.ActivityType,
            activityTypeVersion: root.ActivityTypeVersion,
            descriptor: root.Descriptor,
            inputBindings: root.InputBindings,
            metadata: root.Metadata,
            activityContract: root.ActivityContract,
            childSlots:
            [
                new ExecutableChildSlot("children", children)
            ]);

    private static ExecutableNode NewNode(
        string nodeId,
        JsonElement descriptorPayload,
        ActivityContract? contract = null,
        IReadOnlyDictionary<string, RuntimeInputBinding>? inputBindings = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, descriptorPayload.Clone()),
            inputBindings: inputBindings ?? new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            activityContract: contract ?? EmptyContract(descriptorPayload));

    private static ActivityContract EmptyContract(JsonElement descriptorPayload, IEnumerable<ActivityInputContract>? inputs = null) =>
        new(
            "test/activity",
            "1.0.0",
            "test",
            descriptorPayload,
            inputs ?? [],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/activity"));

    private static ActivityContract SecretInputContract() => InputContract("apiKey", "ApiKey", ActivityValuePolicy.Default);

    private static ActivityContract InputContract(string key, string name, ActivityValuePolicy policy)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return EmptyContract(
            document.RootElement,
            [new ActivityInputContract(key, name, StringType, true, false, false, null, policy)]);
    }

    private static ActivityInputSnapshot EmptySnapshot()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new ActivityInputSnapshot(
            "actexec-1",
            EmptyContract(document.RootElement).SchemaFingerprint,
            "sha256:empty-bindings",
            new Dictionary<string, ValueEnvelope>(),
            DateTimeOffset.UnixEpoch);
    }

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    /// <summary>Evaluates every expression to the same text, standing in for a script engine.</summary>
    private sealed class FixedTextEvaluator : IPortableExpressionEvaluator
    {
        public const string Text = "evaluated note";

        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromResult(JsonSerializer.SerializeToElement(Text));
    }

    /// <summary>
    /// The shipped resolver, except that it throws when asked to resolve a secret read. The binding resolver is
    /// replaceable, so the materializer must withhold a secret read without consulting it at all.
    /// </summary>
    private sealed class SecretReadRefusingResolver : IRuntimeInputBindingResolver
    {
        private readonly RuntimeInputBindingResolver _inner = new();

        public RuntimeResolvedInput Resolve(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context) =>
            binding.Source == RuntimeInputBindingSource.SecretRead
                ? throw new InvalidOperationException($"The materializer asked the binding resolver to resolve secret read '{binding.InputName}'.")
                : _inner.Resolve(binding, context);
    }
}
