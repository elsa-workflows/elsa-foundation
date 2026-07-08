using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// In-process execution coverage for <see cref="HttpEndpoint"/> Result resolution (spec 089 sub-unit A,
/// FR-002) through the real workflow agent on the shared <see cref="WorkflowExecutionHarness"/>. The live
/// request travels on the dedicated stimulus-input channel; the workflow-inputs bag can neither supply nor
/// forge it. Without a valid stimulus payload the Result falls back to the authored-route projection.
/// </summary>
public sealed class HttpEndpointExecutionTests
{
    private const string NodeId = "node-http-endpoint";
    private const string ResultValueId = "http-endpoint-result";

    [Fact]
    public async Task SurfacesLiveRequest_WhenStimulusInputIsSeeded()
    {
        await using var harness = NewHarness();
        var liveRequest = new HttpRequestModel(
            Path: "orders/webhook",
            Method: "POST",
            Headers: new Dictionary<string, string[]> { ["x-test"] = ["abc"] },
            Query: new Dictionary<string, string[]> { ["q"] = ["1"] },
            Body: """{"id":7}""");

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook"), JsonSerializer.SerializeToElement(liveRequest));

        run.AssertCompleted(NodeId);
        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("POST", result.GetProperty(nameof(HttpRequestModel.Method)).GetString());
        Assert.Equal("""{"id":7}""", result.GetProperty(nameof(HttpRequestModel.Body)).GetString());
        Assert.Equal("abc", result.GetProperty(nameof(HttpRequestModel.Headers)).GetProperty("x-test")[0].GetString());
        Assert.Equal("1", result.GetProperty(nameof(HttpRequestModel.Query)).GetProperty("q")[0].GetString());
    }

    [Fact]
    public async Task FallsBackToAuthoredRoute_WhenNoStimulusInputIsSeeded()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(NewEndpointExecutable("Orders/Webhook/"));

        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", result.GetProperty(nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty(nameof(HttpRequestModel.Body)).ValueKind);
    }

    [Theory]
    [InlineData("\"not-a-request-model\"")] // JSON string — not an object
    [InlineData("""{"foo":1}""")] // foreign JSON object — deserializes but has no Path/Method
    [InlineData("""{"Path":"x"}""")] // half-shaped object — Method missing
    public async Task FallsBackToAuthoredRoute_WhenStimulusInputIsForeignOrMalformed(string foreignPayload)
    {
        // A foreign stimulus payload (another trigger module started this artifact) must never surface as a
        // half-populated request model — identifying members (Path, Method) are validated after deserialize.
        await using var harness = NewHarness();
        using var document = JsonDocument.Parse(foreignPayload);

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook"), document.RootElement.Clone());

        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", result.GetProperty(nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal("*", result.GetProperty(nameof(HttpRequestModel.Method)).GetString());
    }

    [Fact]
    public async Task WorkflowInputsBag_CannotForgeTheStimulusInput()
    {
        // Regression pin for the spec-089 review: a caller-supplied workflow input (the execute API's inputs
        // bag) named like the old well-known key must NOT surface as the endpoint's Result — the stimulus
        // travels on its own channel, so the activity falls back to the authored route.
        await using var harness = NewHarness();
        var forged = new HttpRequestModel("evil", "DELETE", new Dictionary<string, string[]>(), new Dictionary<string, string[]>(), "forged");
        var inputs = new Dictionary<string, JsonElement>
        {
            ["stimulusInput"] = JsonSerializer.SerializeToElement(forged)
        };

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook"), inputs);

        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", result.GetProperty(nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal("*", result.GetProperty(nameof(HttpRequestModel.Method)).GetString());
    }

    private static async Task<JsonElement> ResultAsync(WorkflowExecutionHarness harness)
    {
        var value = await harness.Services.GetRequiredService<IDurableValueStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, $"durable-{ResultValueId}");
        Assert.NotNull(value);
        return value!.InlineValue!.Value;
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            .Build("actexec-http-endpoint");

    private static WorkflowExecutable NewEndpointExecutable(string path)
    {
        var node = new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: "authored-http-endpoint",
            activityType: HttpEndpoint.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(Serializer, typeof(HttpEndpoint)),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Path"] = new(
                    inputName: "Path",
                    source: RuntimeInputBindingSource.Literal,
                    literalValue: JsonSerializer.SerializeToElement(path),
                    metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.String" })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                ["Result"] = new(
                    outputName: "Result",
                    valueId: ResultValueId,
                    type: new RuntimeValueTypeDescriptor("clr", "System.Object", null),
                    lifecycle: DurableValueLifecycle.Instance,
                    storage: DurableValueStorage.Inline,
                    captureOnSuccessfulCompletion: true)
            },
            metadata: new Dictionary<string, string>());

        return WorkflowExecutionHarness.NewExecutable(node);
    }

    private static IPayloadSerializer Serializer =>
        new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
}
