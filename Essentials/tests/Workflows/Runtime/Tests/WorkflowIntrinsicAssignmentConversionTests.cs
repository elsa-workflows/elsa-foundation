using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowIntrinsicAssignmentConversionTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private const string IntrinsicExecutionId = "actexec-intrinsic";
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly ValueTypeDescriptor IntType = new("Int32");
    private static readonly WorkflowExecutableIdentity Identity = new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
    private readonly InMemoryWorkflowExecutionStateStore _workflowStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStore = new();
    private readonly PayloadStore _payloadStore = new();
    private readonly InMemoryDurableValueStateStore _durableStore = new();
    private readonly WorkflowIntrinsicExecutor _executor;

    public WorkflowIntrinsicAssignmentConversionTests() =>
        _executor = new WorkflowIntrinsicExecutor(_workflowStore, _activityStore, new RuntimeInputBindingResolver(),
            _durableStore,
            new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore()), new FakeTimeProvider(Now),
            externalPayloadStore: _payloadStore, valueConversionExecutor: new RuntimeValueConversionExecutor(new IntRegistry()),
            inputValidator: new WorkflowExecutableInputValidator(new IntRegistry()));

    [Theory]
    [InlineData("variable", false)]
    [InlineData("variable", true)]
    [InlineData("request", false)]
    [InlineData("request", true)]
    [InlineData("projection", false)]
    [InlineData("projection", true)]
    public async Task Set_converts_a_dynamic_number_before_committing_it(string source, bool external)
    {
        var commit = await ExecuteAsync(Node(source), 7, external);

        var value = commit.StateChanges.WorkflowExecution!.State.RootVariableFrame!.Values["counter"];
        Assert.Equal(IntType, value.Type);
        var payload = value.InlineValue ?? await _payloadStore.ReadAsync(value.ExternalReference!);
        Assert.Equal(JsonValueKind.Number, payload.ValueKind);
        Assert.Equal(7, payload.GetInt32());
    }

    [Theory]
    [InlineData("7", "variable", false)]
    [InlineData("7", "variable", true)]
    [InlineData(2147483648L, "variable", false)]
    [InlineData(true, "variable", false)]
    [InlineData("7", "request", false)]
    [InlineData("7", "request", true)]
    [InlineData("7", "projection", false)]
    [InlineData("7", "projection", true)]
    [InlineData(2147483648L, "projection", false)]
    [InlineData(true, "projection", false)]
    public async Task Set_refuses_an_invalid_dynamic_payload_without_changing_the_destination(object payload, string source, bool external)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(Node(source), payload, external).AsTask());

        var state = await _workflowStore.FindAsync(WorkflowExecutionId);
        Assert.Equal(0, state!.RootVariableFrame!.Revision);
        Assert.Equal(0, state.RootVariableFrame.Values["counter"].InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData("Guid", "d0a3cbdf-97db-49ce-a4b2-ce4236d152a7")]
    [InlineData("DateTime", "2026-10-25T12:00:00Z")]
    [InlineData("TimeSpan", "00:00:07")]
    public async Task Set_preserves_valid_string_encoded_scalars(string alias, string payload)
    {
        var commit = await ExecuteAsync(Node("request", alias), payload, false);

        var value = commit.StateChanges.WorkflowExecution!.State.RootVariableFrame!.Values["counter"];
        Assert.Equal(alias, value.Type.Alias);
        Assert.Equal(payload, value.InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Set_preserves_object_json()
    {
        var commit = await ExecuteAsync(Node("request", "Object"), new { message = "hello" }, false);

        var value = commit.StateChanges.WorkflowExecution!.State.RootVariableFrame!.Values["counter"];
        Assert.Equal("Object", value.Type.Alias);
        Assert.Equal("hello", value.InlineValue!.Value.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Set_executes_a_pinned_conversion_before_validating_and_storing_the_value(bool external)
    {
        var plan = new ValueConversionPlan(ValueConversionPlan.CurrentSchemaVersion, ValueRepresentation.FormattedContent,
            new ValueTypeDescriptor("String"), IntType, ValueConversionMode.Json, ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json", "1"), limits: null, options: null);

        var commit = await ExecuteAsync(Node("variable", plan: plan), "7", external);

        var value = commit.StateChanges.WorkflowExecution!.State.RootVariableFrame!.Values["counter"];
        Assert.Equal(7, value.InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task Set_validates_dynamic_collection_elements()
    {
        var commit = await ExecuteAsync(Node("request", collectionKind: CollectionKind.Array), new[] { 7, 8 }, false);
        var value = commit.StateChanges.WorkflowExecution!.State.RootVariableFrame!.Values["counter"];
        Assert.Equal(CollectionKind.Array, value.Type.CollectionKind);
        Assert.Equal(new[] { 7, 8 }, value.InlineValue!.Value.EnumerateArray().Select(item => item.GetInt32()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ExecuteAsync(Node("request", collectionKind: CollectionKind.Array), new object[] { 7, "8" }, false).AsTask());
        Assert.Equal(0, (await _workflowStore.FindAsync(WorkflowExecutionId))!.RootVariableFrame!.Revision);
    }

    private static ExecutableNode Node(string source, string alias = "Int32", ValueConversionPlan? plan = null, CollectionKind collectionKind = CollectionKind.Single)
    {
        var targetType = new ValueTypeDescriptor(alias, collectionKind);
        var binding = source == "variable"
            ? new RuntimeInputBinding("value", targetType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead,
                variable: new RuntimeVariableReference("source", "workflow"), conversionPlan: plan)
            : new RuntimeInputBinding("value", targetType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequest,
                workflowRequest: new RuntimeWorkflowRequestReference("source", source == "projection" ? "source.Count" : null), conversionPlan: plan);
        return new ExecutableNode("set", "set", "elsa.intrinsic.set", "1.0.0", "intrinsic", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["value"] = binding }, new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set, intrinsicVariable: new RuntimeVariableReference("counter", "workflow"));
    }

    private sealed class PayloadStore : IExternalPayloadStore
    {
        private JsonElement _payload;
        public ValueTask<DurableValueExternalReference> WriteAsync(ExternalPayloadWriteRequest request, CancellationToken cancellationToken = default)
        {
            _payload = request.Payload.Clone();
            return ValueTask.FromResult(new DurableValueExternalReference("test", "source", new Dictionary<string, string>()));
        }
        public ValueTask<JsonElement> ReadAsync(DurableValueExternalReference reference, CancellationToken cancellationToken = default) => ValueTask.FromResult(_payload);
    }

    private sealed class IntRegistry : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias) { alias = "Int32"; return type == typeof(int); }
        public bool TryGetType(string alias, out Type type)
        {
            type = alias switch { "Int32" => typeof(int), "Guid" => typeof(Guid), "DateTime" => typeof(DateTime), "TimeSpan" => typeof(TimeSpan), "Object" => typeof(object), _ => typeof(void) };
            return type != typeof(void);
        }
        public IEnumerable<Type> ListTypes() => [typeof(int)];
        public string GetAliasOrDefault(Type type) => "Int32";
        public Type GetTypeOrDefault(string alias) => TryGetType(alias, out var type) ? type : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }

    private async ValueTask<RuntimeCheckpointCommit> ExecuteAsync(ExecutableNode node, object payloadValue, bool external)
    {
        var scheduled = ActivityState(IntrinsicExecutionId, node.ExecutableNodeId, ActivityExecutionStatus.Scheduled) with { Attempts = [] };
        var isProjection = node.InputBindings["value"].WorkflowRequest?.Path is not null;
        var json = JsonSerializer.SerializeToElement(isProjection ? new { count = payloadValue } : payloadValue);
        var source = ValueEnvelope.Inline(node.InputBindings["value"].ConversionPlan?.SourceType ?? new ValueTypeDescriptor("Elsa.Any"), json, ValueProtectionPolicy.InstanceInline);
        if (external)
        {
            var reference = await _payloadStore.WriteAsync(new ExternalPayloadWriteRequest(WorkflowExecutionId, "source", "test", source.Type, source.InlineValue!.Value, source.Policy));
            source = ValueEnvelope.External(source.Type, reference, source.Policy);
        }
        await _durableStore.SaveAsync(new DurableValueState("source", WorkflowExecutionId, "source",
            new RuntimeValueTypeDescriptor("alias", "Int32", null), DurableValueLifecycle.Instance,
            external ? DurableValueStorage.External : DurableValueStorage.Inline, source.InlineValue, source.ExternalReference, null, Now,
            new Dictionary<string, string> { [RuntimeMetadataKeys.InputName] = "source" }));
        await _workflowStore.SaveAsync(new WorkflowExecutionState(WorkflowExecutionId, Identity, WorkflowExecutionStatus.Running, null, Now, Now, Now, null, null, null, null, new Dictionary<string, string>())
        {
            RootVariableFrame = new VariableFrameFactory().CreateRoot(WorkflowExecutionId, VariableReference.WorkflowScopeId,
                new Dictionary<string, ValueEnvelope> { ["source"] = source, ["counter"] = node.InputBindings["value"].TargetType == IntType ? ValueEnvelope.Inline(IntType, JsonSerializer.SerializeToElement(0), ValueProtectionPolicy.InstanceInline) : ValueEnvelope.Null(node.InputBindings["value"].TargetType, ValueProtectionPolicy.InstanceInline) })
        });
        await _activityStore.SaveAsync(scheduled);
        var payload = new RuntimeStartActivityCommandPayload(Identity, node.ExecutableNodeId, IntrinsicExecutionId, RuntimeStartActivityCommandPayload.ScheduledActivityReason);
        var executable = new WorkflowExecutable(
            Identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(),
            inputContract: null, dependencies: null, runtimeRequirements: null, storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        var workItem = new RuntimeSchedulerWorkItem(
            "start-intrinsic",
            WorkflowExecutionId,
            "command-intrinsic",
            WorkflowExecutionCommandKind.StartActivity,
            "envelope-1",
            $"{WorkflowExecutionId}:start:{IntrinsicExecutionId}",
            Now,
            Now,
            1,
            JsonSerializer.SerializeToElement(payload),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        return await _executor.ExecuteAsync(workItem, payload, executable, node, scheduled);
    }

    private static ActivityExecutionState ActivityState(
        string invocationId,
        string nodeId,
        ActivityExecutionStatus status,
        string? schedulingActivityExecutionId = null) =>
        new(
            new ActivityExecution(invocationId, WorkflowExecutionId, nodeId, nodeId, $"test/{nodeId}", "1.0.0"),
            status,
            null,
            Now.AddSeconds(-1),
            null,
            null,
            schedulingActivityExecutionId,
            null,
            null,
            null,
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>());
}
