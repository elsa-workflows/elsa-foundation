using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// In-process execution coverage for the <c>WriteHttpResponse</c> activity running through the real workflow
/// agent on the shared <see cref="WorkflowExecutionHarness"/>. It proves the async/202 contract concretely: the
/// activity records exactly one <see cref="HttpResponseInstruction"/> into the durable workflow-output channel
/// under <see cref="HttpResponseInstruction.OutputName"/> — observable and assertable immediately after the run,
/// never a silent no-op. The activity is constructed by the production
/// <see cref="Elsa.Activities.Primitives.Constructors.ClrActivityConstructor"/>.
/// </summary>
public sealed class WriteHttpResponseExecutionTests
{
    private const string NodeId = "node-write-http-response";

    [Fact]
    public async Task RecordsResponseInstruction_AsObservableWorkflowOutput()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewWriteNode(statusCode: 201, body: "created", contentType: "application/json")));

        run.AssertCompleted(NodeId);
        run.AssertWorkflowCompleted();

        var output = await SingleResponseOutput(harness);
        Assert.Equal(201, output.GetProperty(nameof(HttpResponseInstruction.StatusCode)).GetInt32());
        Assert.Equal("created", output.GetProperty(nameof(HttpResponseInstruction.Body)).GetString());
        Assert.Equal("application/json", output.GetProperty(nameof(HttpResponseInstruction.ContentType)).GetString());
    }

    [Fact]
    public async Task DefaultsStatusToTwoHundred_WhenNotAuthored()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewWriteNode(statusCode: null, body: "ok", contentType: null)));

        run.AssertWorkflowCompleted();

        var output = await SingleResponseOutput(harness);
        Assert.Equal(200, output.GetProperty(nameof(HttpResponseInstruction.StatusCode)).GetInt32());
    }

    private static async Task<JsonElement> SingleResponseOutput(WorkflowExecutionHarness harness)
    {
        var durableValues = await harness.Services.GetRequiredService<IDurableValueStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        var output = Assert.Single(
            durableValues,
            value => value.Metadata.TryGetValue(RuntimeMetadataKeys.OutputName, out var name)
                && name == HttpResponseInstruction.OutputName);
        return output.InlineValue!.Value;
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            .Build("actexec-write-http-response");

    private static ExecutableNode NewWriteNode(int? statusCode, string? body, string? contentType)
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>();
        if (statusCode is { } status)
            inputBindings["StatusCode"] = LiteralBinding("StatusCode", status, "System.Int32");
        if (body is not null)
            inputBindings["Body"] = LiteralBinding("Body", body, "System.String");
        if (contentType is not null)
            inputBindings["ContentType"] = LiteralBinding("ContentType", contentType, "System.String");

        return new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: "authored-write-http-response",
            activityType: typeof(WriteHttpResponse).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(ClrConstruction.ConsumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, ClrConstruction.Payload(Serializer, typeof(WriteHttpResponse))),
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }

    private static RuntimeInputBinding LiteralBinding(string inputName, object value, string typeName) =>
        new(
            inputName: inputName,
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement(value),
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = typeName });

    private static IPayloadSerializer Serializer =>
        TestPayloadSerializers.NewPayloadSerializer();
}
