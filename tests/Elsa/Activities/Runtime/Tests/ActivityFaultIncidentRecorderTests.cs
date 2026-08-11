using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ActivityFaultIncidentRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);
    private readonly CapturingCheckpointCommitStore _store = new();

    [Fact]
    public async Task CommitAsync_DoesNotCaptureStackTrace_ByDefault()
    {
        var recorder = new ActivityFaultIncidentRecorder(TimeProvider.System);

        await recorder.CommitAsync(NewRequest(CapturedException()));

        var incident = CapturedIncident();
        Assert.False(incident.Metadata.ContainsKey(RuntimeMetadataKeys.FaultStackTrace));
    }

    [Fact]
    public async Task CommitAsync_CapturesStackTrace_WhenPolicyEnablesIt()
    {
        var policy = new DefaultRuntimeFaultCapturePolicy(Options.Create(new RuntimeFaultCaptureOptions { CaptureStackTrace = true }));
        var recorder = new ActivityFaultIncidentRecorder(TimeProvider.System, inspectionAccumulator: null, policy);

        await recorder.CommitAsync(NewRequest(CapturedException()));

        var incident = CapturedIncident();
        Assert.Equal("System.InvalidOperationException", incident.Metadata[RuntimeMetadataKeys.FaultType]);
        Assert.Equal("boom", incident.Metadata[RuntimeMetadataKeys.FaultMessage]);
        Assert.False(string.IsNullOrWhiteSpace(incident.Metadata[RuntimeMetadataKeys.FaultStackTrace]));
    }

    [Fact]
    public async Task CommitAsync_EndsOpenAttemptAndPersistsNormalizedFault()
    {
        var recorder = new ActivityFaultIncidentRecorder(TimeProvider.System);
        var attempt = new ActivityAttempt(
            "attempt-1",
            "actexec-1",
            1,
            ActivityAttemptReason.Initial,
            Now);

        await recorder.CommitAsync(NewRequest(
            new InvalidOperationException("boom"),
            NewRunningState() with { Attempts = [attempt] }));

        var commit = Assert.IsType<RuntimeCheckpointCommit>(_store.Commit);
        var state = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("InputMaterializationFailed", state.Fault!.Code);
        Assert.Equal(typeof(InvalidOperationException).FullName, state.Fault.ExceptionType);
        Assert.Equal("boom", state.Fault.Message);
        var endedAttempt = Assert.Single(state.Attempts!);
        Assert.NotNull(endedAttempt.EndedAt);
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, endedAttempt.TransitionKind);
        Assert.Equal(Assert.Single(state.IncidentIds), endedAttempt.IncidentId);
    }

    [Fact]
    public void IncidentId_StaysWithinProjectionColumnBudget_ForDispatchedExecutionIdShapes()
    {
        // #1031: a dispatched child's work-item and activity-execution ids both embed 'dispatch:v1:<sha-256>'
        // shapes, which pushed the raw-concatenation incident id past the 128-char 'by-incident-id' projection
        // column (GW-PHYSICAL-037) and poisoned the fault-recording checkpoint. The derived id must stay
        // bounded regardless of how long those source ids grow.
        var activityExecutionId = $"dispatch:v1:{new string('a', 64)}:activity:root";
        var workItemId = $"{new string('b', 16)}:invoke:{activityExecutionId}";

        var incidentId = ActivityFaultIncidentRecorder.IncidentId(workItemId, activityExecutionId, "ActivityReturnedFault");

        Assert.True(incidentId.Length <= 128, $"Incident id length {incidentId.Length} exceeds the 128-char column budget.");
        // Deterministic: pre-commit references (child-fault parent evaluation) and replays re-derive the same id.
        Assert.Equal(incidentId, ActivityFaultIncidentRecorder.IncidentId(workItemId, activityExecutionId, "ActivityReturnedFault"));
        // Discriminating: siblings and different fault kinds stay distinct.
        Assert.NotEqual(incidentId, ActivityFaultIncidentRecorder.IncidentId(workItemId, activityExecutionId, "InputMaterializationFailed"));
        Assert.NotEqual(incidentId, ActivityFaultIncidentRecorder.IncidentId(
            workItemId,
            $"dispatch:v1:{new string('c', 64)}:activity:root",
            "ActivityReturnedFault"));
    }

    [Fact]
    public async Task CommitAsync_AppliesFaultCapturePolicyToInnerExceptionMetadata()
    {
        const string secret = "customer-secret-token";
        var recorder = new ActivityFaultIncidentRecorder(
            TimeProvider.System,
            inspectionAccumulator: null,
            new ScrubbingFaultCapturePolicy());

        await recorder.CommitAsync(NewRequest(
            new InvalidOperationException("Expression evaluation failed", new Exception(secret))));

        var commit = Assert.IsType<RuntimeCheckpointCommit>(_store.Commit);
        var incident = Assert.Single(commit.StateChanges.Incidents).State;
        var activity = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Equal("[scrubbed]", incident.Metadata[RuntimeMetadataKeys.FaultInnerMessage]);
        Assert.Equal("[scrubbed]", activity.Metadata[RuntimeMetadataKeys.FaultInnerMessage]);
        Assert.DoesNotContain(secret, string.Join('\n', incident.Metadata.Values), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join('\n', activity.Metadata.Values), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sensitive_JavaScript_failure_cannot_reach_durable_incident_metadata()
    {
        const string secret = "customer-secret-token";
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();
        var sensitivePolicy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            isSensitive: true,
            redactionMode: "Full");
        var expression = new RuntimeExpressionBinding(
            "JavaScript",
            "(() => { throw new Error(args.secret); })()",
            parameters: new Dictionary<string, ExpressionParameterBinding>
            {
                ["secret"] = new WorkflowRequestExpressionParameterBinding("secret")
            });
        var binding = new RuntimeInputBinding(
            "message",
            new ValueTypeDescriptor("String"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: expression);
        var materializer = new RuntimeActivityInputMaterializer(
            new RuntimeInputBindingResolver(),
            new StringTypeRegistry(),
            evaluator);
        var context = new RuntimeInputBindingResolutionContext(
            "wfexec-1",
            "actexec-1",
            workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
            {
                ["secret"] = ValueEnvelope.Inline(
                    new ValueTypeDescriptor("String"),
                    JsonSerializer.SerializeToElement(secret),
                    sensitivePolicy)
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => materializer
            .MaterializeSnapshotAsync(NewExpressionNode(binding), "actexec-1", context, Now)
            .AsTask());
        var recorder = new ActivityFaultIncidentRecorder(TimeProvider.System);
        await recorder.CommitAsync(NewRequest(exception));

        var commit = Assert.IsType<RuntimeCheckpointCommit>(_store.Commit);
        var incident = Assert.Single(commit.StateChanges.Incidents).State;
        var activity = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Contains("was redacted", incident.Metadata[RuntimeMetadataKeys.FaultInnerMessage], StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join('\n', incident.Metadata.Values), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join('\n', activity.Metadata.Values), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sensitive_external_provider_cancellation_shaped_failure_cannot_reach_durable_incident_metadata()
    {
        const string secret = "external-provider-secret-token";
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();
        var sensitivePolicy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "Full");
        var reference = new DurableValueExternalReference("encrypted", "payloads/secret", new Dictionary<string, string>());
        var expression = new RuntimeExpressionBinding(
            "JavaScript",
            "args.secret",
            parameters: new Dictionary<string, ExpressionParameterBinding>
            {
                ["secret"] = new WorkflowRequestExpressionParameterBinding("secret")
            });
        var binding = new RuntimeInputBinding(
            "message",
            new ValueTypeDescriptor("String"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: expression);
        var materializer = new RuntimeActivityInputMaterializer(
            new RuntimeInputBindingResolver(),
            new StringTypeRegistry(),
            evaluator,
            new SecretThrowingExternalPayloadStore(secret));
        var context = new RuntimeInputBindingResolutionContext(
            "wfexec-1",
            "actexec-1",
            workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
            {
                ["secret"] = ValueEnvelope.External(new ValueTypeDescriptor("String"), reference, sensitivePolicy)
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => materializer
            .MaterializeSnapshotAsync(NewExpressionNode(binding), "actexec-1", context, Now)
            .AsTask());
        var recorder = new ActivityFaultIncidentRecorder(TimeProvider.System);
        await recorder.CommitAsync(NewRequest(exception));

        var commit = Assert.IsType<RuntimeCheckpointCommit>(_store.Commit);
        var incident = Assert.Single(commit.StateChanges.Incidents).State;
        var activity = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Contains("was redacted", incident.Metadata[RuntimeMetadataKeys.FaultInnerMessage], StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join('\n', incident.Metadata.Values), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join('\n', activity.Metadata.Values), StringComparison.Ordinal);
    }

    private ActivityFaultIncidentRecordRequest NewRequest(Exception exception, ActivityExecutionState? state = null)
    {
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), _store, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
        return new ActivityFaultIncidentRecordRequest(
            CheckpointCommitter: committer,
            WorkItem: NewWorkItem(),
            ActivityExecutionId: "actexec-1",
            ExecutableNodeId: "node-1",
            State: state ?? NewRunningState(),
            Exception: exception,
            SubStatus: "InputMaterializationFailed",
            ActivityMetadata: new Dictionary<string, string>(),
            IncidentMetadata: new Dictionary<string, string>());
    }

    private IncidentState CapturedIncident()
    {
        var commit = Assert.IsType<RuntimeCheckpointCommit>(_store.Commit);
        var change = Assert.Single(commit.StateChanges.Incidents);
        return change.State;
    }

    private static RuntimeSchedulerWorkItem NewWorkItem() =>
        new(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:invoke:actexec-1",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-1",
                AuthoredActivityId: "authored-node-1",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: Now,
            StartedAt: Now,
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

    private static Exception CapturedException()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private static ExecutableNode NewExpressionNode(RuntimeInputBinding binding)
    {
        using var descriptor = JsonDocument.Parse("{}");
        var contract = new ActivityContract(
            "test/activity",
            "1.0.0",
            "test",
            descriptor.RootElement,
            [new ActivityInputContract("message", "Message", new ValueTypeDescriptor("String"), true, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/activity"));
        return new ExecutableNode(
            "node-1",
            "authored-node-1",
            "test/activity",
            "1.0.0",
            "test",
            descriptor.RootElement,
            new Dictionary<string, RuntimeInputBinding> { ["message"] = binding },
            new Dictionary<string, string>(),
            activityContract: contract);
    }

    private sealed class StringTypeRegistry : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias)
        {
            alias = "String";
            return type == typeof(string);
        }

        public bool TryGetType(string alias, out Type type) => TryGetTypeOrDefault(alias, out type);
        public IEnumerable<Type> ListTypes() => [typeof(string)];
        public string GetAliasOrDefault(Type type) => type == typeof(string) ? "String" : type.FullName!;
        public Type GetTypeOrDefault(string alias) => TryGetTypeOrDefault(alias, out var type) ? type : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type)
        {
            type = typeof(string);
            return StringComparer.Ordinal.Equals(alias, "String");
        }
    }

    private sealed class SecretThrowingExternalPayloadStore(string secret) : IExternalPayloadStore
    {
        public ValueTask<DurableValueExternalReference> WriteAsync(
            ExternalPayloadWriteRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<JsonElement> ReadAsync(
            DurableValueExternalReference reference,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<JsonElement>(new OperationCanceledException(secret));
    }

    private sealed class CapturingCheckpointCommitStore : IRuntimeCheckpointCommitStore
    {
        public RuntimeCheckpointCommit? Commit { get; private set; }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            Commit = commit;
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }
    }

    private sealed class ScrubbingFaultCapturePolicy : IRuntimeFaultCapturePolicy
    {
        public RuntimeFaultInfo Capture(Exception exception) =>
            new(exception.GetType().FullName ?? exception.GetType().Name, "[scrubbed]", StackTrace: null);
    }
}
