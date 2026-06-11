using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();

    [Fact]
    public async Task HandleAsync_InvokesRunningActivityAndRecordsCompletedState()
    {
        var activity = new RecordingActivity();
        var factory = new RecordingActivityFactory(activity);
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal("hello", activity.ObservedText);
        Assert.Equal("actexec-1", activity.Id);
        Assert.Equal("node-start", activity.NodeId);
        Assert.Equal("test", factory.LastDescriptorType);
        Assert.Single(factory.LastInputs);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Null(state.SubStatus);
        Assert.Equal(_now, state.CompletedAt);
        Assert.Equal(RuntimeInvokeActivityCommandPayload.StartedActivityReason, state.Metadata["runtime.invokeReason"]);
        Assert.Equal("invoke-work", state.Metadata["runtime.invokeSchedulerWorkItemId"]);
    }

    [Fact]
    public async Task HandleAsync_RecordsCompletedSkippedStateWhenCanExecuteReturnsFalse()
    {
        var activity = new RecordingActivity { ShouldExecute = false };
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(new RecordingActivityFactory(activity));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Null(activity.ObservedText);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Equal("Skipped", state.SubStatus);
        Assert.Equal(bool.TrueString, state.Metadata["runtime.invokeSkipped"]);
    }

    [Fact]
    public async Task HandleAsync_RecordsFaultedStateWhenActivityThrows()
    {
        var activity = new RecordingActivity { Exception = new InvalidOperationException("boom") };
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(new RecordingActivityFactory(activity));
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ActivityFaulted", state.SubStatus);
        Assert.Equal(_now, state.CompletedAt);
        Assert.Equal(1, state.FaultCount);
        Assert.Equal(1, state.AggregateFaultCount);
        Assert.Equal(typeof(InvalidOperationException).FullName, state.Metadata["runtime.faultType"]);
        Assert.Equal("boom", state.Metadata["runtime.faultMessage"]);
    }

    [Fact]
    public async Task HandleAsync_PropagatesConstructionFailureWithoutChangingRunningState()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        var factory = new ThrowingActivityFactory(new InvalidOperationException("missing constructor"));
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewInvokeWorkItem(NewIdentity())).AsTask());

        Assert.Equal("missing constructor", exception.Message);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.Null(state.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_DoesNotOverwriteExistingLaterLifecycleState()
    {
        var activity = new RecordingActivity();
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState() with
        {
            Status = ActivityExecutionStatus.Completed,
            CompletedAt = _now.AddMinutes(-1)
        });
        var factory = new RecordingActivityFactory(activity);
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        await handler.HandleAsync(NewInvokeWorkItem(NewIdentity()));

        Assert.Equal(0, factory.CreateCalls);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Equal(_now.AddMinutes(-1), state.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_RejectsPayloadNodeMismatchBeforeInvokingActivity()
    {
        var activity = new RecordingActivity();
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        var factory = new RecordingActivityFactory(activity);
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewInvokeWorkItem(NewIdentity(), executableNodeId: "node-other")).AsTask());

        Assert.Contains("belongs to executable node 'node-start'", exception.Message);
        Assert.Equal(0, factory.CreateCalls);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingPayloadBeforeInvokingActivity()
    {
        var factory = new RecordingActivityFactory(new RecordingActivity());
        await _activityStateStore.SaveAsync(NewRunningState());
        await using var provider = NewProvider(factory);
        var handler = NewHandler(provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewInvokeWorkItem(includePayload: false)).AsTask());

        Assert.Contains("requires an invoke activity payload", exception.Message);
        Assert.Equal(0, factory.CreateCalls);
    }

    [Fact]
    public void CanHandle_AcceptsOnlyInvokeActivityWork()
    {
        using var provider = NewProvider(new RecordingActivityFactory(new RecordingActivity()));
        var handler = NewHandler(provider);

        Assert.True(handler.CanHandle(NewInvokeWorkItem(NewIdentity())));
        Assert.False(handler.CanHandle(NewInvokeWorkItem(NewIdentity(), commandKind: WorkflowExecutionCommandKind.StartActivity)));
    }

    private WorkflowInvokeActivitySchedulerWorkHandler NewHandler(ServiceProvider provider) =>
        new(
            new RuntimeActivityInputMaterializer(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(_now));

    private ServiceProvider NewProvider(IActivityFactory factory)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => factory);
        services.AddSingleton(_executableStore);
        services.AddSingleton<IWorkflowExecutableStore>(_ => _executableStore);
        services.AddSingleton(_activityStateStore);
        services.AddSingleton<IActivityExecutionStateStore>(_ => _activityStateStore);
        return services.BuildServiceProvider();
    }

    private RuntimeSchedulerWorkItem NewInvokeWorkItem(
        WorkflowExecutableIdentity? pinnedExecutable = null,
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.InvokeActivity,
        string executableNodeId = "node-start",
        JsonElement? payload = null,
        bool includePayload = true)
    {
        var resolvedPayload = includePayload
            ? payload ?? JsonSerializer.SerializeToElement(new RuntimeInvokeActivityCommandPayload(
                pinnedExecutable ?? NewIdentity(),
                executableNodeId,
                "actexec-1",
                RuntimeInvokeActivityCommandPayload.StartedActivityReason))
            : (JsonElement?)null;

        return new RuntimeSchedulerWorkItem(
            workItemId: "invoke-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:invoke:actexec-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 30,
            payload: resolvedPayload,
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-start",
                AuthoredActivityId: "authored-node-start",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
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
            Metadata: new Dictionary<string, string> { ["runtime.startReason"] = "test" });

    private static WorkflowExecutable NewExecutable()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var start = NewNode("node-start", document.RootElement);
        var other = NewNode("node-other", document.RootElement);

        return new(
            identity: NewIdentity(),
            nodes: [start, other],
            edges: [],
            startNodeIds: ["node-start"],
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static ExecutableNode NewNode(string nodeId, JsonElement descriptorPayload) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: descriptorPayload.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Text"] = new(
                    inputName: "Text",
                    source: RuntimeInputBindingSource.Literal,
                    literalValue: JsonSerializer.SerializeToElement("hello"),
                    metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = $"{typeof(string).FullName}, {typeof(string).Assembly.GetName().Name}" })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class RecordingActivityFactory(IActivity activity) : IActivityFactory
    {
        public int CreateCalls { get; private set; }
        public string? LastDescriptorType { get; private set; }
        public IDictionary<string, InputArgument> LastInputs { get; private set; } = new Dictionary<string, InputArgument>();

        public ValueTask<IActivity> Create(
            string descriptorType,
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            LastDescriptorType = descriptorType;
            LastInputs = inputs ?? new Dictionary<string, InputArgument>();
            if (activity is RecordingActivity recordingActivity && LastInputs.TryGetValue("Text", out var text))
                recordingActivity.Text = (InputArgument<string>)text;

            return ValueTask.FromResult(activity);
        }
    }

    private sealed class ThrowingActivityFactory(Exception exception) : IActivityFactory
    {
        public ValueTask<IActivity> Create(
            string descriptorType,
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class RecordingActivity : ActivityBase
    {
        public InputArgument<string> Text { get; set; } = null!;
        public bool ShouldExecute { get; set; } = true;
        public Exception? Exception { get; set; }
        public string? ObservedText { get; private set; }

        protected override ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) =>
            ValueTask.FromResult(ShouldExecute);

        protected override void Execute(IActivityExecutionContext context)
        {
            if (Exception is not null)
                throw Exception;

            ObservedText = context.Get(Text);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
