using System.Net;
using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Http;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>
/// SendHttpRequest is the activity whose dynamic outcome ports motivated this whole exercise. The drive reaches
/// every static outcome — Done on a 2xx, Failed on a transport error <em>and</em> on a non-2xx without expected
/// codes, Timeout when the linked timeout fires — and, with ExpectedStatusCodes authored, both a per-status port
/// and the unmatched catch-all. All of it against a stubbed transport, so no network is touched.
/// </summary>
public sealed class SendHttpRequestDrive : IActivityDrive
{
    private const string NodeId = "node-send-http";

    public Type ActivityType => typeof(SendHttpRequest);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunAsync(recorder, _ => Respond(HttpStatusCode.OK, "hello"), null, ActivityOutcomes.Done);
        await RunAsync(recorder, _ => Respond(HttpStatusCode.InternalServerError, "boom"), null, HttpActivityOutcomes.Failed);
        await RunAsync(recorder, _ => throw new HttpRequestException("no route to host"), null, HttpActivityOutcomes.Failed);

        // The linked timeout CTS fires while the handler is still waiting, so only the request is cancelled.
        await RunAsync(
            recorder,
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                throw new UnreachableException();
            },
            null,
            HttpActivityOutcomes.Timeout,
            timeout: TimeSpan.FromMilliseconds(50));

        // Authored expected codes: a match takes the port named after the status, a non-match the catch-all.
        await RunAsync(recorder, _ => Respond(HttpStatusCode.Created, "made"), [201, 202], "201");
        await RunAsync(recorder, _ => Respond(HttpStatusCode.Accepted, "later"), [201], HttpActivityOutcomes.UnmatchedStatusCode);
    }

    private Task RunAsync(
        ActivityDriveRecorder recorder,
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        int[]? expectedStatusCodes,
        string expectedOutcome,
        TimeSpan? timeout = null) =>
        RunAsync(recorder, (request, _) => Task.FromResult(responder(request)), expectedStatusCodes, expectedOutcome, timeout);

    private async Task RunAsync(
        ActivityDriveRecorder recorder,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        int[]? expectedStatusCodes,
        string expectedOutcome,
        TimeSpan? timeout = null)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            .WithFeature(services => services
                .AddHttpClient(HttpActivityConstants.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(responder)))
            .Build("actexec-http");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode(expectedStatusCodes, timeout)));

        recorder.Record(ActivityType, run, NodeId);
        run.AssertOutcomes(NodeId, expectedOutcome);
    }

    /// <summary>
    /// Builds the node with an explicit contract, because the per-status ports are <em>per-node</em> data the
    /// publish compiler pins from the authored ExpectedStatusCodes — the harness's attribute-only pinning would
    /// not know about them, and the runtime rejects an outcome the node's contract does not declare.
    /// </summary>
    private static ExecutableNode NewSendNode(int[]? expectedStatusCodes, TimeSpan? timeout)
    {
        var inputs = new Dictionary<string, (object Value, Type Type)>
        {
            ["Url"] = (new Uri("https://example.test/resource"), typeof(Uri)),
            ["Method"] = ("GET", typeof(string))
        };
        if (expectedStatusCodes is not null)
            inputs["ExpectedStatusCodes"] = (expectedStatusCodes, typeof(ICollection<int>));
        if (timeout is { } value)
            inputs["Timeout"] = (value, typeof(TimeSpan?));

        var bindings = inputs.ToDictionary(
            entry => entry.Key,
            entry => LiteralBinding(entry.Key, entry.Value.Value, entry.Value.Type),
            StringComparer.Ordinal);
        var contracts = inputs.Select(entry => new ActivityInputContract(
            entry.Key,
            entry.Key,
            ValueType(entry.Value.Type),
            isRequired: entry.Key == "Url",
            isNullable: entry.Key != "Url",
            hasDefault: false,
            defaultValue: null,
            ActivityValuePolicy.Default));

        var alias = TypeAliasConvention.CanonicalAlias(typeof(SendHttpRequest));
        var descriptorPayload = ClrConstruction.Payload(TestPayloadSerializers.NewPayloadSerializer(), typeof(SendHttpRequest));
        var contract = new ActivityContract(
            alias,
            "1.0.0",
            ClrConstruction.DescriptorType,
            descriptorPayload,
            contracts,
            new ActivityResultContract(
                new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(SendHttpRequestResult))),
                isRequired: true,
                ActivityValuePolicy.Default,
                []),
            Outcomes(expectedStatusCodes),
            new ActivityActivationRequirement(ClrConstruction.DescriptorType, alias));

        return new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: "authored-send-http",
            activityType: alias,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: descriptorPayload,
            inputBindings: bindings,
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }

    // Mirrors ExecutableNodeCompiler.ResolveOutcomes: static base outcomes, plus one per authored code and the
    // unmatched catch-all when ExpectedStatusCodes is supplied (#926).
    private static string[] Outcomes(int[]? expectedStatusCodes)
    {
        var outcomes = new List<string> { ActivityOutcomes.Done, HttpActivityOutcomes.Failed, HttpActivityOutcomes.Timeout };
        if (expectedStatusCodes is { Length: > 0 })
        {
            outcomes.AddRange(expectedStatusCodes.Select(code => code.ToString()));
            outcomes.Add(HttpActivityOutcomes.UnmatchedStatusCode);
        }

        return outcomes.ToArray();
    }

    private static RuntimeInputBinding LiteralBinding(string key, object value, Type type)
    {
        var valueType = ValueType(type);
        return new RuntimeInputBinding(
            key,
            valueType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(valueType, JsonSerializer.SerializeToElement(value, type), ValueProtectionPolicy.InstanceInline));
    }

    private static ValueTypeDescriptor ValueType(Type type)
    {
        var reference = TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias);
        return new ValueTypeDescriptor(reference.Alias, reference.CollectionKind);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }

    private sealed class UnreachableException() : Exception("The stub responder should never return.");
}

/// <summary>
/// WriteHttpResponse returns one atomic response instruction. All four of its outputs are driven with real
/// values so none of them can pass as "declared but never written".
/// </summary>
public sealed class WriteHttpResponseDrive : IActivityDrive
{
    public Type ActivityType => typeof(WriteHttpResponse);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            .Build("actexec-write-response");

        var node = Nodes.Leaf("node-write-response", typeof(WriteHttpResponse), new Dictionary<string, object?>
        {
            ["StatusCode"] = 201,
            ["Body"] = "created",
            ["ContentType"] = "text/plain",
            ["Headers"] = new Dictionary<string, string[]> { ["X-Driven"] = ["yes"] }
        });

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(node));

        recorder.Record(ActivityType, run, "node-write-response");
        run.AssertOutcomes("node-write-response", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }
}

/// <summary>
/// HttpEndpoint as a trigger delivery: the run carries a real HttpRequestModel on the stimulus channel, which is
/// what populates the Request, RouteData and ParsedContent outputs. Driving it any other way suspends instead,
/// and the outputs would read as dead.
/// </summary>
public sealed class HttpEndpointDrive : IActivityDrive
{
    public Type ActivityType => typeof(HttpEndpoint);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        const string nodeId = "node-endpoint";
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            // HttpEndpoint derives ParsedContent from the raw body through this parser; the Http feature leaves it
            // to the host, so the drive registers the production implementation.
            .WithFeature(services => services.AddSingleton<Elsa.Http.Core.Contracts.IHttpRequestBodyParser, Elsa.Http.Services.HttpRequestBodyParser>())
            .Build("actexec-endpoint");

        var node = Nodes.Leaf(nodeId, typeof(HttpEndpoint), new Dictionary<string, object?>
        {
            ["Path"] = "/orders/{id}",
            ["CanStartWorkflow"] = true
        });

        var request = JsonSerializer.SerializeToElement(new HttpRequestModel(
            Path: "/orders/7",
            Method: "POST",
            Headers: new Dictionary<string, string[]> { ["Content-Type"] = ["application/json"] },
            Query: new Dictionary<string, string[]> { ["verbose"] = ["true"] },
            Body: """{"total":42}""",
            RouteData: new Dictionary<string, string> { ["id"] = "7" }));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(node), request, triggerNodeId: nodeId);

        recorder.Record(ActivityType, run, nodeId);
        run.AssertOutcomes(nodeId, ActivityOutcomes.Done);
        Assert.Equal(ActivityExecutionStatus.Completed, run.State(nodeId).Status);
    }
}
