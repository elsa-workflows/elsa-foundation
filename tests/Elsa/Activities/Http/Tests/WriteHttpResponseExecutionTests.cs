using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
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
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.DoesNotContain(durableValues, value => value.Metadata.ContainsKey(RuntimeMetadataKeys.OutputName));
    }

    [Fact]
    public async Task DefaultsStatusToTwoHundred_WhenNotAuthored()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewWriteNode(statusCode: null, body: "ok", contentType: null)));

        var state = run.AssertCompleted(NodeId);
        var result = Assert.IsType<JsonElement>(state.Completion?.Result.InlineValue);
        Assert.Equal(200, result.GetProperty("statusCode").GetInt32());
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            .Build("actexec-write-http-response");

    private static ExecutableNode NewWriteNode(int? statusCode, string? body, string? contentType)
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(WriteHttpResponse.StatusCode)] = LiteralBinding(nameof(WriteHttpResponse.StatusCode), statusCode ?? 200, typeof(int)),
            [nameof(WriteHttpResponse.Body)] = LiteralBinding(nameof(WriteHttpResponse.Body), body, typeof(string)),
            [nameof(WriteHttpResponse.ContentType)] = LiteralBinding(nameof(WriteHttpResponse.ContentType), contentType, typeof(string)),
            [nameof(WriteHttpResponse.Headers)] = LiteralBinding(nameof(WriteHttpResponse.Headers), null, typeof(IDictionary<string, string[]>))
        };

        var activityType = TypeAliasConvention.CanonicalAlias(typeof(WriteHttpResponse));
        var descriptorPayload = ClrConstruction.Payload(Serializer, typeof(WriteHttpResponse));
        var inputContracts = new[]
        {
            InputContract(nameof(WriteHttpResponse.StatusCode), typeof(int)),
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
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }

    private static ActivityInputContract InputContract(string key, Type type) =>
        new(
            key,
            key,
            ValueType(type),
            isRequired: false,
            hasDefault: false,
            defaultValue: null,
            ActivityValuePolicy.Default);

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
}
