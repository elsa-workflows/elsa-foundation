using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class RuntimeOutputCaptureGraphPolicyRegressionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly ValueTypeDescriptor Text = new("String");
    private static readonly ActivityExecutionState Boundary = State("boundary", "graph");

    [Fact]
    public async Task A_public_capture_keeps_the_graph_destinations_sensitivity_and_the_overwritten_values_own_policy()
    {
        var store = new InMemoryDurableValueStateStore();
        var driver = new SpyDriver();
        var projector = new RuntimeOutputCaptureProjector(new RuntimeDurableValueStorageDriverRegistry([driver]));
        var sensitive = ActivityValuePolicy.Default with { IsSensitive = true };
        var first = Scenario("first", "private-value", sensitive, driver.DriverKey);
        var firstProjection = await Capture(projector, store, first, State("first", "producer"));
        var firstRow = Assert.Single(firstProjection.DurableValues).State!;
        await store.SaveAsync(firstRow);

        // This second capture was based on version zero, before the first capture committed.
        // A default-mode lost update retains operational overwritten-value history.
        var second = Scenario("second", "replacement", ActivityValuePolicy.Default, driver.DriverKey);
        var invocation = State("second", "producer") with
        {
            InputSnapshot = new ActivityInputSnapshot("second", second.Node.ActivityContract!.SchemaFingerprint,
                "bindings", new Dictionary<string, ValueEnvelope>(), Now)
            {
                InputReadSet = new VariableInputReadSet([new VariableReadSetEntry("graph", "value", 0)])
            }
        };
        var secondProjection = await Capture(projector, store, second, invocation);

        var written = Assert.Single(secondProjection.DurableValues).State!;
        Assert.True(DurableValuePolicies.Read(written).IsSensitive);
        Assert.Equal("replacement", written.InlineValue!.Value.GetString());
        var history = Assert.Single(secondProjection.VariableWrites);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, history.Outcome);
        Assert.NotNull(history.OverwrittenValue);
        Assert.True(history.OverwrittenValue.Policy.IsSensitive);
        Assert.Equal("private-value", history.OverwrittenValue.InlineValue!.Value.GetString());
        Assert.Equal("first", history.OverwrittenWriter!.InvocationId);
        Assert.Null(history.Digest);
    }

    [Fact]
    public async Task An_encrypted_null_destination_refuses_a_non_null_capture_before_the_external_capable_encoder_runs()
    {
        var store = new InMemoryDurableValueStateStore();
        var driver = new SpyDriver(externalizeNonNull: true);
        var projector = new RuntimeOutputCaptureProjector(new RuntimeDurableValueStorageDriverRegistry([driver]));
        // Null has no payload to encrypt. This driver encodes null inline, but would externalize a scalar.
        var encrypted = ActivityValuePolicy.Default with { IsSensitive = true, RequiresEncryption = true };
        var first = Scenario("first", null, encrypted, driver.DriverKey);
        var seeded = Assert.Single((await Capture(projector, store, first, State("first", "producer"))).DurableValues).State!;
        await store.SaveAsync(seeded);
        Assert.True(DurableValuePolicies.Read(seeded).RequiresEncryption);
        driver.Encoded.Clear();
        driver.ExternalWrites.Clear();

        var next = Scenario("second", "must-not-reach-storage", ActivityValuePolicy.Default, driver.DriverKey);
        await Assert.ThrowsAsync<WithheldValueException>(() => Capture(projector, store, next, State("second", "producer")));

        Assert.Empty(driver.Encoded);
        Assert.Empty(driver.ExternalWrites);
        var unchanged = await store.FindAsync("workflow", seeded.DurableValueId);
        Assert.NotNull(unchanged);
        Assert.True(DurableValuePolicies.Read(unchanged).RequiresEncryption);
        Assert.Equal(JsonValueKind.Null, unchanged.InlineValue!.Value.ValueKind);
        Assert.Null(unchanged.ExternalReference);
    }

    private static Task<RuntimeOutputCaptureProjection> Capture(RuntimeOutputCaptureProjector projector,
        IDurableValueStateStore store,
        (ExecutableNode Node, IActivityCompletionTransition Transition, ActivityCompletionProjection Completion) scenario,
        ActivityExecutionState invocation) => projector.ProjectAsync("workflow", invocation.InvocationId,
            scenario.Node, scenario.Transition, scenario.Completion, Now,
            closedScopeBoundary: Boundary, invocation: invocation, durableValueStateStore: store).AsTask();

    private static (ExecutableNode Node, IActivityCompletionTransition Transition, ActivityCompletionProjection Completion)
        Scenario(string invocationId, string? value, ActivityValuePolicy policy, string driverKey)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { });
        var contract = new ActivityContract("test.text", "1", "test", descriptor, [],
            new ActivityResultContract(Text, false, policy,
                [new ActivityResultProjectionContract("result", "$", Text, false, policy)]),
            ["Done"], new ActivityActivationRequirement("test", "test.text"));
        var transition = ActivityTransition.Complete<string?>(value);
        var completion = new ActivityCompletionProjector().Project(invocationId,
            new ActivityAttempt("attempt-" + invocationId, invocationId, 1, ActivityAttemptReason.Initial, Now),
            contract, transition, Now);
        var capture = new RuntimeOutputCapture("result", "unused-graph-address",
            new RuntimeValueTypeDescriptor("String", driverKey, JsonSerializer.SerializeToElement(new TypeReference("String"))),
            DurableValueLifecycle.Instance, DurableValueStorage.Custom, true,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.CaptureTargetScopeId] = "graph",
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = "value",
                [RuntimeMetadataKeys.VariableName] = "value"
            }, storageDriverKey: driverKey);
        var node = new ExecutableNode("producer", "producer", "test.text", "1", "test", descriptor,
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            activityContract: contract,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture> { ["result"] = capture });
        return (node, (IActivityCompletionTransition)transition, completion);
    }

    private static ActivityExecutionState State(string invocationId, string nodeId) => new(
        new(invocationId, "workflow", nodeId, nodeId, "test", "1"), ActivityExecutionStatus.Running,
        null, 1, Now, Now, null, null, null, null, null,
        new(null, null, null, null, null, null, null, null, new Dictionary<string, string>()),
        null, [], [], 0, 0, new Dictionary<string, string>());

    private sealed class SpyDriver(bool externalizeNonNull = false) : IRuntimeDurableValueStorageDriver
    {
        private readonly JsonRuntimeDurableValueStorageDriver _json = new();
        public string DriverKey => "test.graph-policy-spy";
        public List<object?> Encoded { get; } = [];
        public List<JsonElement> ExternalWrites { get; } = [];
        public ValueTask<RuntimeDurableValueEncoding> EncodeAsync(object? value, RuntimeValueTypeDescriptor type,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Encoded.Add(value);
            if (externalizeNonNull && value is not null && value is not JsonElement { ValueKind: JsonValueKind.Null })
            {
                ExternalWrites.Add(JsonSerializer.SerializeToElement(value));
                return ValueTask.FromResult(new RuntimeDurableValueEncoding(DurableValueStorage.External, null,
                    new DurableValueExternalReference("archive", "written", new Dictionary<string, string>())));
            }
            return _json.EncodeAsync(value, type, cancellationToken);
        }
        public ValueTask<object?> DecodeAsync(DurableValueState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These capture tests never decode through the driver.");
    }
}
