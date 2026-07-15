using System.Net;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Primitives;
using Elsa.Activities.Testing;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// In-process execution coverage for the <c>SendHttpRequest</c> activity running through the real workflow
/// agent on the shared <see cref="WorkflowExecutionHarness"/>. The activity is constructed by the production
/// <see cref="Elsa.Activities.Primitives.Constructors.ClrActivityConstructor"/> (registered by
/// <see cref="ActivitiesPrimitivesFeature"/>), and the outbound transport is stubbed by overriding the named
/// client's primary handler — so the tests exercise the descriptor → construct → bind → execute path and the
/// activity's outcome mapping without a live network.
/// </summary>
public sealed class SendHttpRequestExecutionTests
{
    private const string NodeId = "node-send-http";

    [Fact]
    public async Task SuccessfulResponse_WithoutExpectedCodes_EmitsDoneOutcome()
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.OK, "hello"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode()));

        run.AssertOutcomes(NodeId, ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Response_MatchingExpectedCode_BranchesOnStatusCodeOutcome()
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.Created, "made"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewSendNode(expectedStatusCodes: new[] { 201, 202 })));

        run.AssertOutcomes(NodeId, "201");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Response_NotMatchingExpectedCode_BranchesOnUnmatchedOutcome()
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.OK, "ok"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewSendNode(expectedStatusCodes: new[] { 201 })));

        run.AssertOutcomes(NodeId, HttpActivityOutcomes.Unmatched);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task ErrorStatus_WithoutExpectedCodes_EmitsFailedOutcome()
    {
        await using var harness = NewHarness(_ => Respond(HttpStatusCode.InternalServerError, "boom"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode()));

        run.AssertOutcomes(NodeId, HttpActivityOutcomes.Failed);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task TransportFailure_EmitsFailedOutcome()
    {
        await using var harness = NewHarness(_ => throw new HttpRequestException("no route to host"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode()));

        run.AssertOutcomes(NodeId, HttpActivityOutcomes.Failed);
        run.AssertWorkflowCompleted();
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private static WorkflowExecutionHarness NewHarness(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        WorkflowExecutionHarness.Create()
            // SerializationFeature + ActivitiesPrimitivesFeature provide the CLR activity constructor; the Http
            // feature configures the named client; the final registration overrides its primary handler with the
            // stub so no live network is touched (last ConfigurePrimaryHttpMessageHandler wins).
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            .WithFeature(services => services
                .AddHttpClient(HttpActivityConstants.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(responder)))
            .Build("actexec-http");

    private static ExecutableNode NewSendNode(int[]? expectedStatusCodes = null)
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["Url"] = LiteralBinding("Url", new Uri("https://example.test/resource"), "System.Uri"),
            ["Method"] = LiteralBinding("Method", "GET", "System.String")
        };

        if (expectedStatusCodes is not null)
            inputBindings["ExpectedStatusCodes"] = LiteralBinding(
                "ExpectedStatusCodes",
                expectedStatusCodes,
                "System.Collections.Generic.ICollection`1[[System.Int32]]");

        return new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: "authored-send-http",
            activityType: typeof(SendHttpRequest).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(ClrConstruction.ConsumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, ClrConstruction.Payload(Serializer, typeof(SendHttpRequest))),
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

    private static Elsa.Serialization.Core.IPayloadSerializer Serializer =>
        TestPayloadSerializers.NewPayloadSerializer();

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
