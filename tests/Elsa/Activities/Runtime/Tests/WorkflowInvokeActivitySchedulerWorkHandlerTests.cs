using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryIncidentStateStore _incidentStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _checkpointWriter;

    public WorkflowInvokeActivitySchedulerWorkHandlerTests()
    {
        _checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: null,
            activityExecutionStateStore: _activityStateStore,
            bookmarkStateStore: null,
            durableValueStateStore: _durableValueStateStore,
            incidentStateStore: _incidentStateStore,
            operationalStateStore: null,
            schedulerStateStore: null,
            activityExecutionInspectionWriter: _inspectionStore);
    }

    [Fact]
    public async Task HandleAsync_TypedNode_UsesActivationLeaseAndCommitsReturnedResultAtomically()
    {
        var activator = new RecordingTypedActivator();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal(1, activator.ActivateCalls);
        Assert.True(activator.Activity!.Disposed);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal("hello", state!.InputSnapshot!.Values["text"].InlineValue!.Value.GetString());
        Assert.Equal("Done", state.Completion?.OutcomeKey);
        Assert.Equal(5, state.Completion?.Result.InlineValue!.Value.GetProperty("length").GetInt32());
        Assert.NotNull(Assert.Single(state.Attempts!).EndedAt);
        await AssertCompletionWorkAsync();
    }

    [Fact]
    public async Task HandleAsync_TypedNode_CommitsReturnedFaultAsFaultTransition()
    {
        var activator = new ReturningTypedActivator(ActivityTransition.Fault<TypedResult>(
            new ActivityFault("payment.declined", "The payment was declined", isRetryable: false)));
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, state!.Status);
        Assert.Equal("payment.declined", state.Fault!.Code);
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, Assert.Single(state.Attempts!).TransitionKind);
        Assert.True(activator.Activity!.Disposed);
    }

    [Fact]
    public async Task HandleAsync_TypedNode_CommitsReturnedCancellationAndSchedulesWorkflowCancellation()
    {
        var activator = new ReturningTypedActivator(ActivityTransition.Cancel<TypedResult>("Caller disconnected"));
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Cancelled, state!.Status);
        Assert.Equal("Caller disconnected", state.Metadata[RuntimeMetadataKeys.CancellationReason]);
        var commit = Assert.Single(_checkpointWriter.ListCommits()).Commit;
        var cancelWork = Assert.Single(commit.PostCommitIntents).Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.Cancel, cancelWork.CommandKind);
    }

    [Fact]
    public async Task HandleAsync_ReplayedTypedInvocation_UsesCommittedCompletionWithoutReactivation()
    {
        var activator = new RecordingTypedActivator();
        await _executableStore.SaveAsync(NewTypedExecutable());
        await _activityStateStore.SaveAsync(NewTypedRunningState());
        await using var provider = NewProvider(activator, includeInspection: true);
        var handler = NewHandler(provider);
        var workItem = NewInvokeWorkItem(NewIdentity());

        await handler.HandleAsync(workItem);
        var committed = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        await handler.HandleAsync(workItem);

        Assert.Equal(1, activator.ActivateCalls);
        Assert.Equal(committed, await _activityStateStore.FindAsync("wfexec-1", "actexec-1"));
        Assert.Single(_checkpointWriter.ListCommits());
    }

    private WorkflowInvokeActivitySchedulerWorkHandler NewHandler(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(_now));

    private async Task<RuntimeCompleteActivityCommandPayload> AssertCompletionWorkAsync()
    {
        var intents = _checkpointWriter.ListCommits().SelectMany(write => write.Commit.PostCommitIntents).ToArray();
        var work = intents.Length == 0
            ? Assert.Single(await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")))
            : Assert.Single(intents).Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, work.CommandKind);
        return work.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
    }

    private ServiceProvider NewProvider(IActivityActivator activityActivator, bool includeInspection = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityActivator>(activityActivator);
        services.AddSingleton<IWorkflowExecutionStateStore>(_ =>
            CanonicalWorkflowStateTestData.EnsureRunning(new InMemoryWorkflowExecutionStateStore()));
        services.AddSingleton<IWorkflowExecutableStore>(_ => _executableStore);
        services.AddSingleton<IActivityExecutionStateStore>(_ => _activityStateStore);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(_ => _schedulerWorkQueue);
        services.AddSingleton<IDurableValueStateStore>(_ => _durableValueStateStore);
        services.AddSingleton<IIncidentStateStore>(_ => _incidentStateStore);
        services.AddSingleton<IRuntimeCheckpointCommitStore>(_ => _checkpointWriter);
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(_now));
        services.AddSingleton<IRuntimePostCommitIntentDispatcher, RuntimeSchedulerPostCommitIntentDispatcher>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton(sp => new ActivityFaultIncidentRecorder(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IRuntimeActivityExecutionInspectionAccumulator>()));
        services.AddSingleton<ActivityCompletionProjector>();
        services.AddSingleton<ActivityInputHydrator>();
        services.AddSingleton<IRuntimeExecutionIdGenerator, ShortRuntimeExecutionIdGenerator>();
        if (includeInspection)
        {
            services.AddSingleton<IActivityExecutionInspectionStore>(_ => _inspectionStore);
            services.AddSingleton<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
            services.AddSingleton<IRuntimePayloadCapturePolicy, DefaultRuntimePayloadCapturePolicy>();
        }
        return services.BuildServiceProvider();
    }

    private RuntimeSchedulerWorkItem NewInvokeWorkItem(WorkflowExecutableIdentity? pinnedExecutable = null) =>
        new(
            "invoke-work",
            "wfexec-1",
            "command-1",
            WorkflowExecutionCommandKind.InvokeActivity,
            "envelope-1",
            "wfexec-1:invoke:actexec-1",
            _now,
            _now,
            30,
            JsonSerializer.SerializeToElement(new RuntimeInvokeActivityCommandPayload(
                pinnedExecutable ?? NewIdentity(), "node-start", "actexec-1", RuntimeInvokeActivityCommandPayload.StartedActivityReason)),
            new Dictionary<string, string> { ["source"] = "test" },
            new Dictionary<string, string> { ["transport"] = "in-process" });

    private ActivityExecutionState NewTypedRunningState()
    {
        var contract = NewTypedExecutable().RootActivity.ActivityContract!;
        var snapshot = new ActivityInputSnapshot(
            "actexec-1",
            contract.SchemaFingerprint,
            "sha256:bindings",
            new Dictionary<string, ValueEnvelope>
            {
                ["text"] = ValueEnvelope.Inline(new Elsa.Primitives.Models.ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("hello"), ValueProtectionPolicy.InstanceInline)
            },
            _now);
        return new ActivityExecutionState(
            new ActivityExecution("actexec-1", "wfexec-1", "node-start", "authored-node-start", "test/typed", "1.0.0"),
            ActivityExecutionStatus.Running,
            null,
            _now,
            _now,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>())
        {
            ContractIdentity = new ActivityInvocationContractIdentity(contract.ActivityTypeKey, contract.ContractVersion, contract.SchemaFingerprint),
            InputSnapshot = snapshot,
            Attempts = [new ActivityAttempt("actexec-1:attempt:1", "actexec-1", 1, ActivityAttemptReason.Initial, _now)]
        };
    }

    private static WorkflowExecutable NewTypedExecutable(string literalText = "hello")
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "typed" });
        var contract = new ActivityContract(
            "test/typed",
            "1.0.0",
            "typed",
            descriptor,
            [new ActivityInputContract("text", "Text", new Elsa.Primitives.Models.ValueTypeDescriptor("String"), true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(
                new Elsa.Primitives.Models.ValueTypeDescriptor("test/typed-result"),
                true,
                ActivityValuePolicy.Default,
                [new ActivityResultProjectionContract("length", "length", new Elsa.Primitives.Models.ValueTypeDescriptor("Int32"), true, ActivityValuePolicy.Default)]),
            ["Done"],
            new ActivityActivationRequirement("typed", "test/typed"));
        var type = new Elsa.Primitives.Models.ValueTypeDescriptor("String");
        var input = new RuntimeInputBinding(
            "text", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(literalText), ValueProtectionPolicy.InstanceInline));
        var node = new ExecutableNode(
            "node-start",
            "authored-node-start",
            "test/typed",
            "1.0.0",
            "typed",
            descriptor,
            new Dictionary<string, RuntimeInputBinding> { ["text"] = input },
            new Dictionary<string, RuntimeOutputCapture>
            {
                ["length"] = new("length", "node-start:result:length", new RuntimeValueTypeDescriptor("alias", "Int32", null), DurableValueLifecycle.Instance, DurableValueStorage.Inline, true)
            },
            new Dictionary<string, string>(),
            activityContract: contract);
        return new WorkflowExecutable(NewIdentity(), node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>());
    }

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class RecordingTypedActivator : IActivityActivator
    {
        public int ActivateCalls { get; private set; }
        public TypedCompletingActivity? Activity { get; private set; }
        public List<ActivityActivationRequest> Requests { get; } = [];

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            ActivateCalls++;
            Requests.Add(request);
            Activity = new TypedCompletingActivity(request.Inputs.Values["text"].InlineValue!.Value.GetString()!);
            return ValueTask.FromResult(new ActivityActivationLease(Activity));
        }
    }

    private sealed class TypedCompletingActivity(string text) : Activity<TypedResult>, IDisposable
    {
        public bool Disposed { get; private set; }
        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new TypedResult(text.Length)));
        public void Dispose() => Disposed = true;
    }

    private sealed class ReturningTypedActivator(ActivityTransition<TypedResult> transition) : IActivityActivator
    {
        public ReturningTypedActivity? Activity { get; private set; }
        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            Activity = new ReturningTypedActivity(transition);
            return ValueTask.FromResult(new ActivityActivationLease(Activity));
        }
    }

    private sealed class ReturningTypedActivity(ActivityTransition<TypedResult> transition) : Activity<TypedResult>, IDisposable
    {
        public bool Disposed { get; private set; }
        protected override ValueTask<ActivityTransition<TypedResult>> ExecuteAsync(ActivityExecutionContext context) => ValueTask.FromResult(transition);
        public void Dispose() => Disposed = true;
    }

    private sealed record TypedResult(int Length);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
