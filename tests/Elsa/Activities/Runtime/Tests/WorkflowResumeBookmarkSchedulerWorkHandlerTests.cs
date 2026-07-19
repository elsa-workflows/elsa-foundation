using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
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

public sealed partial class WorkflowResumeBookmarkSchedulerWorkHandlerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 17, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryBookmarkStateStore _bookmarkStateStore = new();
    private readonly InMemoryWorkflowSchedulerWorkQueue _schedulerWorkQueue = new();
    private readonly InMemoryIncidentStateStore _incidentStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryDurableValueStateStore _durableValueStateStore = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _checkpointWriter;

    public WorkflowResumeBookmarkSchedulerWorkHandlerTests()
    {
        _checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: null,
            activityExecutionStateStore: _activityStateStore,
            bookmarkStateStore: _bookmarkStateStore,
            durableValueStateStore: null,
            incidentStateStore: _incidentStateStore,
            operationalStateStore: null,
            schedulerStateStore: null,
            activityExecutionInspectionWriter: _inspectionStore);
    }

    private WorkflowResumeBookmarkSchedulerWorkHandler NewHandler(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(_now));

    private Task<RuntimeCompleteActivityCommandPayload> AssertCompletionWorkAsync()
    {
        var intent = Assert.Single(_checkpointWriter.ListCommits().SelectMany(write => write.Commit.PostCommitIntents));
        var work = intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, work.CommandKind);
        return Task.FromResult(work.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!);
    }

    private ServiceProvider NewProvider(
        IActivityActivator activityActivator,
        IBookmarkLifecycleObserver? bookmarkLifecycleObserver = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityActivator>(activityActivator);
        services.AddSingleton<IWorkflowExecutableStore>(_ => _executableStore);
        services.AddSingleton<IActivityExecutionStateStore>(_ => _activityStateStore);
        services.AddSingleton<IWorkflowExecutionStateStore>(_ =>
            CanonicalWorkflowStateTestData.EnsureRunning(new InMemoryWorkflowExecutionStateStore()));
        services.AddSingleton<IBookmarkStateStore>(_ => _bookmarkStateStore);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(_ => _schedulerWorkQueue);
        services.AddSingleton<IDurableValueStateStore>(_ => _durableValueStateStore);
        services.AddSingleton<IIncidentStateStore>(_ => _incidentStateStore);
        services.AddSingleton<IActivityExecutionInspectionStore>(_ => _inspectionStore);
        services.AddSingleton<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(_ => _checkpointWriter);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(_now));
        services.AddSingleton<IRuntimePostCommitIntentDispatcher, RuntimeSchedulerPostCommitIntentDispatcher>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        if (bookmarkLifecycleObserver is not null)
            services.AddSingleton(bookmarkLifecycleObserver);
        services.AddSingleton<BookmarkLifecycleNotifier>();
        services.AddSingleton(sp => new ActivityFaultIncidentRecorder(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IRuntimeActivityExecutionInspectionAccumulator>(),
            DefaultRuntimeFaultCapturePolicy.CreateDefault()));
        services.AddSingleton<IBookmarkConsumptionCheckpointService, BookmarkConsumptionCheckpointService>();
        services.AddSingleton<ActivityCompletionProjector>();
        services.AddSingleton<IRuntimeDurableValueStorageDriver, JsonRuntimeDurableValueStorageDriver>();
        services.AddSingleton<IRuntimeDurableValueStorageDriverRegistry, RuntimeDurableValueStorageDriverRegistry>();
        services.AddSingleton<RuntimeOutputCaptureProjector>();
        return services.BuildServiceProvider();
    }

    private ValueTask<BookmarkState> SaveBookmarkAsync(
        string resumeTargetId = "resume-target:delivery",
        string stimulusType = "delivery-status",
        string stimulusHash = "sha256:delivery-status:order-123") =>
        _bookmarkStateStore.SaveAsync(new BookmarkState(
            "bookmark-1", "wfexec-1", "actexec-1", "node-wait", resumeTargetId,
            stimulusType, stimulusHash, null, new Dictionary<string, string>(), _now.AddMinutes(-1), null));

    private RuntimeSchedulerWorkItem NewResumeWorkItem(
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.ResumeBookmark,
        string resumeTargetId = "resume-target:delivery",
        JsonElement? input = null,
        RuntimeTypedTriggerDeliveryMetadata? triggerDelivery = null)
    {
        var payload = JsonSerializer.SerializeToElement(new RuntimeResumeBookmarkCommandPayload(
            NewIdentity(), "bookmark-1", "actexec-1", "node-wait", resumeTargetId,
            "delivery-status", "sha256:delivery-status:order-123",
            input ?? Json("""{"orderId":"order-123"}"""), RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason, triggerDelivery));
        return new RuntimeSchedulerWorkItem(
            "resume-work", "wfexec-1", "command-1", commandKind, "envelope-1",
            "wfexec-1:resume:bookmark-1", _now, _now, 40, payload,
            new Dictionary<string, string> { ["source"] = "test" },
            new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static ActivityExecutionState NewSuspendedState() =>
        new(
            new ActivityExecution("actexec-1", "wfexec-1", "node-wait", "authored-node-wait", "test/activity", "1.0.0"),
            ActivityExecutionStatus.Suspended,
            "BookmarkWaiting",
            DateTimeOffset.UtcNow.AddMinutes(-3),
            DateTimeOffset.UtcNow.AddMinutes(-2),
            null,
            null,
            null,
            null,
            null,
            null,
            ["bookmark-1"],
            [],
            0,
            0,
            new Dictionary<string, string>());

    private static WorkflowExecutable NewTypedExecutable(string currentLiteral)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "typed-resume" });
        var valueType = new Elsa.Primitives.Models.ValueTypeDescriptor("String");
        var contract = new ActivityContract(
            "test/typed-resume", "1.0.0", "typed-resume", descriptor,
            [new ActivityInputContract("text", "Text", valueType, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new Elsa.Primitives.Models.ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("typed-resume", "test/typed-resume"));
        var binding = new RuntimeInputBinding(
            "text", valueType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(valueType, JsonSerializer.SerializeToElement(currentLiteral), ValueProtectionPolicy.InstanceInline));
        var node = new ExecutableNode(
            "node-wait", "authored-node-wait", "test/typed-resume", "1.0.0", "typed-resume", descriptor,
            new Dictionary<string, RuntimeInputBinding> { ["text"] = binding },
            new Dictionary<string, string>(),
            activityContract: contract);
        return new WorkflowExecutable(
            NewIdentity(),
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-target:delivery"] = new("resume-target:delivery", "node-wait", "test-handler", new Dictionary<string, string>())
            },
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>());
    }

    private ActivityExecutionState NewTypedSuspendedState(ActivityContract contract, string pinnedText)
    {
        var valueType = new Elsa.Primitives.Models.ValueTypeDescriptor("String");
        return NewSuspendedState() with
        {
            ContractIdentity = new ActivityInvocationContractIdentity(contract.ActivityTypeKey, contract.ContractVersion, contract.SchemaFingerprint),
            InputSnapshot = new ActivityInputSnapshot(
                "actexec-1",
                contract.SchemaFingerprint,
                "sha256:original-bindings",
                new Dictionary<string, ValueEnvelope>
                {
                    ["text"] = ValueEnvelope.Inline(valueType, JsonSerializer.SerializeToElement(pinnedText), ValueProtectionPolicy.InstanceInline)
                },
                _now.AddMinutes(-2)),
            Attempts =
            [
                new ActivityAttempt(
                    "actexec-1:attempt:1", "actexec-1", 1, ActivityAttemptReason.Initial,
                    _now.AddMinutes(-2), _now.AddMinutes(-1),
                    transitionKind: Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend)
            ]
        };
    }

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class RecordingTypedResumeActivator : IActivityActivator
    {
        public List<ActivityActivationRequest> Requests { get; } = [];
        public List<TypedResumeTargetActivity> Activities { get; } = [];

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var activity = new TypedResumeTargetActivity(request.Inputs.Values["text"].InlineValue!.Value.GetString()!);
            Activities.Add(activity);
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
    }

    private sealed class TypedResumeTargetActivity(string text) : Activity, IDisposable
    {
        public string? ObservedText { get; private set; }
        public string? ObservedAttemptId { get; private set; }
        public bool Disposed { get; private set; }

        [ResumeTarget("resume-target:delivery")]
        private void Resume(IActivityExecutionContext context)
        {
            ObservedText = text;
            ObservedAttemptId = ((SimpleActivityExecutionContext)context).AttemptId;
        }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));

        public void Dispose() => Disposed = true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
