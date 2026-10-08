using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// In-process execution coverage proving that <see cref="WriteHttpResponse"/> is transiently constructed,
/// hydrated from a pinned input snapshot, and atomically commits one typed completion result.
/// </summary>
public sealed class WriteHttpResponseExecutionTests
{
    private const string NodeId = "node-write-http-response";

    [Fact]
    public async Task CommitsResponseInstruction_AsTypedActivityResult()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewWriteNode(statusCode: 201, body: "created", contentType: "application/json")));

        var state = run.AssertCompleted(NodeId);
        run.AssertWorkflowCompleted();

        var result = Assert.IsType<JsonElement>(state.Completion?.Result.InlineValue);
        Assert.Equal(201, result.GetProperty("statusCode").GetInt32());
        Assert.Equal("created", result.GetProperty("body").GetString());
        Assert.Equal("application/json", result.GetProperty("contentType").GetString());

        var durableValues = await harness.Services.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IDurableValueStateStore>()
            .ListAllDurableValueStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.DoesNotContain(durableValues, value => value.Metadata.ContainsKey(RuntimeMetadataKeys.OutputName));
    }

    [Fact]
    public async Task UnassignedStatusCodeUsesTheActivityDefault()
    {
        var activity = new WriteHttpResponse { Body = "ok" };

        var transition = await ((IActivity)activity).ExecuteAsync(
            new ActivityExecutionContext("wf-1", "invocation-1", "attempt-1", NodeId, CancellationToken.None));

        Assert.Equal(200, Assert.IsAssignableFrom<IActivityCompletionTransition<HttpResponseInstruction>>(transition).Result.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonpositiveStatusCodeDefaultsToTwoHundred(int statusCode)
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewWriteNode(statusCode, body: "ok", contentType: "text/plain")));

        var state = run.AssertCompleted(NodeId);
        var result = Assert.IsType<JsonElement>(state.Completion?.Result.InlineValue);
        Assert.Equal(200, result.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task ObjectHeaderValuesAreSerializedAsTheResponseInstruction()
    {
        await using var harness = NewHarness();
        var headers = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["X-Custom"] = ["first", "second"]
        };

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewWriteNode(202, body: "accepted", contentType: "text/plain", headers)));

        var state = run.AssertCompleted(NodeId);
        var result = Assert.IsType<JsonElement>(state.Completion?.Result.InlineValue);
        var values = result.GetProperty("headers").GetProperty("X-Custom");
        Assert.Equal(["first", "second"], values.EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task WorkflowRequestExternalBodyIsDereferencedAndExecutedAsTheResponseInstruction()
    {
        var bodyType = ValueType(typeof(string));
        var reference = new DurableValueExternalReference("request-payloads", "requests/body-1", new Dictionary<string, string>());
        var policy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            metadata: new Dictionary<string, string> { [ValuePolicyCombiner.StorageProfileMetadataKey] = "request-payloads" });
        var binding = new RuntimeInputBinding(
            nameof(WriteHttpResponse.Body),
            bodyType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference("body"));
        var node = NewWriteNode(202, body: null, contentType: "text/plain", bodyBinding: binding);
        var externalStore = new FixedExternalPayloadStore(reference, JsonSerializer.SerializeToElement("from-external-request"));
        var context = new RuntimeInputBindingResolutionContext(
            "wf-1",
            "invocation-1",
            workflowInputEnvelopes: new Dictionary<string, ValueEnvelope>
            {
                ["body"] = ValueEnvelope.External(bodyType, reference, policy)
            });

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), externalStore)
            .MaterializeSnapshotAsync(node, "invocation-1", context, WorkflowExecutionHarness.Timestamp);

        Assert.Equal(reference, snapshot.Values[nameof(WriteHttpResponse.Body)].ExternalReference);
        Assert.Null(snapshot.Values[nameof(WriteHttpResponse.Body)].InlineValue);
        var completion = await ActivateAndProjectAsync(node, snapshot, externalStore);

        Assert.Equal("from-external-request", completion.GetProperty("body").GetString());
        Assert.Equal(reference, Assert.Single(externalStore.Reads));
    }

    [Fact]
    public async Task SuppliedCausallyCompletedStringResultIsMaterializedAndExecutedAsTheResponseBody()
    {
        var completedAt = WorkflowExecutionHarness.Timestamp.AddSeconds(1);
        var producer = ActivityState(
            invocationId: "producer-1",
            nodeId: "prior-string-result",
            status: ActivityExecutionStatus.Completed,
            predecessorId: null) with
        {
            Completion = new ActivityCompletion(
                "producer-1",
                "attempt-producer",
                ValueEnvelope.Inline(
                    ValueType(typeof(string)),
                    JsonSerializer.SerializeToElement("from-prior-activity"),
                    ValueProtectionPolicy.InstanceInline),
                "Done",
                completedAt,
                "contract"),
            CompletedAt = completedAt
        };
        var consumer = ActivityState(
            invocationId: "consumer-1",
            nodeId: NodeId,
            status: ActivityExecutionStatus.Running,
            predecessorId: producer.InvocationId);
        var binding = new RuntimeInputBinding(
            nameof(WriteHttpResponse.Body),
            ValueType(typeof(string)),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference(
                "prior-string-result",
                "$result",
                "scope:root"));
        var context = new RuntimeInputBindingResolutionContext(
            "wf-1",
            consumer.InvocationId,
            consumerInvocation: consumer,
            runtimeView: [producer, consumer]);
        var node = NewWriteNode(203, body: null, contentType: "text/plain", bodyBinding: binding);

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver())
            .MaterializeSnapshotAsync(node, consumer.InvocationId, context, WorkflowExecutionHarness.Timestamp);
        var completion = await ActivateAndProjectAsync(node, snapshot);

        Assert.Equal("from-prior-activity", snapshot.Values[nameof(WriteHttpResponse.Body)].InlineValue!.Value.GetString());
        Assert.Equal("from-prior-activity", completion.GetProperty("body").GetString());
    }

    [Fact]
    public async Task RuntimeInputMaterializerRejectsANonBindingPureExpressionOnTheResponseActivity()
    {
        var bodyBinding = new RuntimeInputBinding(
            nameof(WriteHttpResponse.Body),
            ValueType(typeof(string)),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "Date.now()", capabilityProfile: "unrestricted"));
        var node = NewWriteNode(200, body: null, contentType: "text/plain", bodyBinding: bodyBinding);
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(typeof(string), "String");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), registry)
                .MaterializeSnapshotAsync(
                    node,
                    "invocation-1",
                    new RuntimeInputBindingResolutionContext("wf-1", "invocation-1"),
                    WorkflowExecutionHarness.Timestamp)
                .AsTask());

        Assert.Contains("requires capability profile 'binding-pure-v1'", exception.Message, StringComparison.Ordinal);
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            .Build("actexec-write-http-response");

    private static ExecutableNode NewWriteNode(
        int statusCode,
        string? body,
        string? contentType,
        IDictionary<string, string[]>? headers = null,
        RuntimeInputBinding? bodyBinding = null)
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(WriteHttpResponse.StatusCode)] = LiteralBinding(nameof(WriteHttpResponse.StatusCode), statusCode, typeof(int)),
            [nameof(WriteHttpResponse.Body)] = bodyBinding ?? LiteralBinding(nameof(WriteHttpResponse.Body), body, typeof(string)),
            [nameof(WriteHttpResponse.ContentType)] = LiteralBinding(nameof(WriteHttpResponse.ContentType), contentType, typeof(string)),
            [nameof(WriteHttpResponse.Headers)] = LiteralBinding(nameof(WriteHttpResponse.Headers), headers, typeof(IDictionary<string, string[]>))
        };

        var activityType = TypeAliasConvention.CanonicalAlias(typeof(WriteHttpResponse));
        var descriptorPayload = ClrConstruction.Payload(Serializer, typeof(WriteHttpResponse));
        var inputContracts = new[]
        {
            InputContract(
                nameof(WriteHttpResponse.StatusCode),
                typeof(int),
                hasDefault: true,
                defaultValue: JsonSerializer.SerializeToElement(200)),
            InputContract(nameof(WriteHttpResponse.Body), typeof(string)),
            InputContract(nameof(WriteHttpResponse.ContentType), typeof(string)),
            InputContract(nameof(WriteHttpResponse.Headers), typeof(IDictionary<string, string[]>))
        };
        var contract = new ActivityContract(
            activityType,
            "1.0.0",
            ClrConstruction.DescriptorType,
            descriptorPayload,
            inputContracts,
            new ActivityResultContract(
                ValueType(typeof(HttpResponseInstruction)),
                isRequired: true,
                ActivityValuePolicy.Default,
                []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(ClrConstruction.DescriptorType, activityType));

        return new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: "authored-write-http-response",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: descriptorPayload,
            inputBindings: inputBindings,
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }

    private static ActivityInputContract InputContract(
        string key,
        Type type,
        bool hasDefault = false,
        JsonElement? defaultValue = null) =>
        new(
            key,
            key,
            ValueType(type),
            isRequired: false,
            isNullable: true,
            hasDefault: hasDefault,
            defaultValue: defaultValue,
            policy: ActivityValuePolicy.Default);

    private static ActivityExecutionState ActivityState(
        string invocationId,
        string nodeId,
        ActivityExecutionStatus status,
        string? predecessorId) =>
        new(
            new ActivityExecution(invocationId, "wf-1", nodeId, nodeId, $"test/{nodeId}", "1.0.0"),
            status,
            null,
            1,
            WorkflowExecutionHarness.Timestamp,
            WorkflowExecutionHarness.Timestamp,
            null,
            predecessorId,
            null,
            null,
            null,
            ActivitySchedulingProvenance.From(
                "wf-1",
                null,
                predecessorId,
                null,
                null,
                "path:root",
                "scope:root",
                "test"),
            0,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>());

    private static RuntimeInputBinding LiteralBinding(string inputName, object? value, Type type)
    {
        var valueType = ValueType(type);
        return new RuntimeInputBinding(
            inputKey: inputName,
            targetType: valueType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Literal,
            literal: value is null
                ? ValueEnvelope.Null(valueType, ValueProtectionPolicy.InstanceInline)
                : ValueEnvelope.Inline(
                    valueType,
                    JsonSerializer.SerializeToElement(value, type),
                    ValueProtectionPolicy.InstanceInline));
    }

    private static ValueTypeDescriptor ValueType(Type type) =>
        TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias) is { } reference
            ? new ValueTypeDescriptor(reference.Alias, reference.CollectionKind)
            : throw new InvalidOperationException($"Could not describe '{type}'.");

    private static IPayloadSerializer Serializer => TestPayloadSerializers.NewPayloadSerializer();

    private static async Task<JsonElement> ActivateAndProjectAsync(
        ExecutableNode node,
        ActivityInputSnapshot snapshot,
        IExternalPayloadStore? externalPayloadStore = null)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var activityType = typeof(WriteHttpResponse);
        var activityTypeKey = TypeAliasConvention.CanonicalAlias(activityType);
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(activityType, activityTypeKey);
        var attempt = new ActivityAttempt("attempt-1", snapshot.InvocationId, 1, ActivityAttemptReason.Initial, WorkflowExecutionHarness.Timestamp);
        var activator = new ActivityActivator(
            [new ClrActivityActivator(
                services.GetRequiredService<IServiceScopeFactory>(),
                registry,
                Serializer,
                new GlobalPersistenceAccessContextAccessor())],
            new ActivityInputHydrator(),
            new ActivitySecretInputResolver(null!, null!, null!),
            externalPayloadStore);
        var descriptor = new RuntimeActivityDescriptor(
            WellKnownRuntimeActivityConsumers.ClrActivity,
            RuntimeActivityDescriptor.InitialSchemaVersion,
            node.DescriptorPayload);
        var contract = node.ActivityContract;
        Assert.NotNull(contract);
        await using var lease = await activator.ActivateAsync(new ActivityActivationRequest(
            "wf-1",
            contract,
            snapshot,
            attempt,
            Descriptor: descriptor));
        var transition = await lease.Activity.ExecuteAsync(new ActivityExecutionContext(
            "wf-1",
            snapshot.InvocationId,
            attempt.AttemptId,
            node.ExecutableNodeId,
            CancellationToken.None));
        var projected = new ActivityCompletionProjector().Project(
            snapshot.InvocationId,
            attempt,
            contract,
            transition,
            WorkflowExecutionHarness.Timestamp);

        return projected.Completion.Result.InlineValue!.Value.Clone();
    }

    private sealed class GlobalPersistenceAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Global;
    }

    private sealed class FixedExternalPayloadStore(
        DurableValueExternalReference expectedReference,
        JsonElement payload) : IExternalPayloadStore
    {
        public List<DurableValueExternalReference> Reads { get; } = [];

        public ValueTask<DurableValueExternalReference> WriteAsync(
            ExternalPayloadWriteRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This response-input fixture reads an existing external payload only.");

        public ValueTask<JsonElement> ReadAsync(
            DurableValueExternalReference reference,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedReference, reference);
            Reads.Add(reference);
            return ValueTask.FromResult(payload.Clone());
        }
    }
}
