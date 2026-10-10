using System.Net;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Shared arrangement for the <c>SendHttpRequest</c> suites: a one-node executable over the production CLR
/// constructor, the harness composition with <see cref="ActivitiesHttpFeature"/>, and the stub transport.
/// </summary>
internal static class SendHttpRequestTestSupport
{
    /// <summary>
    /// The category the named client's logger writes under (the default client handler's category), written out so a
    /// change to the production constant turns the tests red.
    /// </summary>
    public const string ClientLoggerCategory = "System.Net.Http.HttpClient.Elsa.Activities.Http.ClientHandler";

    public const string NodeId = "node-send-http";
    public const string ActivityExecutionId = "actexec-http";

    /// <summary>
    /// A header value built at run time from a Guid (a scheme, a space and a plain word), so no credential-shaped literal
    /// sits in source and every call is distinct.
    /// </summary>
    public static string NewHeaderValue(string scheme = "Bearer") => $"{scheme} canary-{Guid.NewGuid():N}";

    public static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    // SerializationFeature + ActivitiesPrimitivesFeature provide the CLR activity constructor; the Http feature
    // configures the named client with its own primary handler.
    public static WorkflowExecutionHarness.Builder NewBuilder() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services));

    // Overrides the named client's primary handler with the stub so no live network is touched (the last
    // ConfigurePrimaryHttpMessageHandler wins).
    public static Action<IServiceCollection> StubTransport(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        services => services
            .AddHttpClient(HttpActivityConstants.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(responder));

    public static WorkflowExecutionHarness NewStubbedHarness(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        NewBuilder().WithFeature(StubTransport(responder)).Build(ActivityExecutionId);

    internal static ExecutableNode NewSendNode(
        Uri? url = null,
        int[]? expectedStatusCodes = null,
        string? authorization = null,
        IDictionary<string, string>? requestHeaders = null)
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["Url"] = LiteralBinding("Url", url ?? new Uri("https://example.test/resource"), typeof(Uri)),
            ["Method"] = LiteralBinding("Method", "GET", typeof(string))
        };

        if (expectedStatusCodes is not null)
            inputBindings["ExpectedStatusCodes"] = LiteralBinding(
                "ExpectedStatusCodes",
                expectedStatusCodes,
                typeof(ICollection<int>));

        if (authorization is not null)
            inputBindings["Authorization"] = LiteralBinding("Authorization", authorization, typeof(string));

        if (requestHeaders is not null)
            inputBindings["RequestHeaders"] = LiteralBinding("RequestHeaders", requestHeaders, typeof(IDictionary<string, string>));

        var activityType = TypeAliasConvention.CanonicalAlias(typeof(SendHttpRequest));
        var resultType = TypeAliasConvention.CanonicalAlias(typeof(SendHttpRequestResult));
        var descriptorPayload = ClrConstruction.Payload(Serializer, typeof(SendHttpRequest));
        var inputContracts = inputBindings.Keys.Select(key => new ActivityInputContract(
            key,
            key,
            key switch
            {
                "Url" => ValueType(typeof(Uri)),
                "Method" => ValueType(typeof(string)),
                "ExpectedStatusCodes" => ValueType(typeof(ICollection<int>)),
                "Authorization" => ValueType(typeof(string)),
                "RequestHeaders" => ValueType(typeof(IDictionary<string, string>)),
                _ => throw new InvalidOperationException($"Unknown test input '{key}'.")
            },
            isRequired: key == "Url",
            isNullable: key != "Url",
            hasDefault: false,
            defaultValue: null,
            ActivityValuePolicy.Default));
        var contract = new ActivityContract(
            activityType,
            "1.0.0",
            ClrConstruction.DescriptorType,
            descriptorPayload,
            inputContracts,
            new ActivityResultContract(
                new ValueTypeDescriptor(resultType),
                isRequired: true,
                ActivityValuePolicy.Default,
                []),
            OutcomesFor(expectedStatusCodes),
            new ActivityActivationRequirement(ClrConstruction.DescriptorType, activityType));

        return new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: "authored-send-http",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: descriptorPayload,
            inputBindings: inputBindings,
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }

    // Mirrors ExecutableNodeCompiler.ResolveOutcomes: the static base outcomes plus, when expected codes are
    // authored, one outcome per code and the unmatched-status-code catch-all (issue #926).
    internal static string[] OutcomesFor(int[]? expectedStatusCodes)
    {
        var outcomes = new List<string> { ActivityOutcomes.Done, HttpActivityOutcomes.Failed, HttpActivityOutcomes.Timeout };
        if (expectedStatusCodes is { Length: > 0 })
        {
            outcomes.AddRange(expectedStatusCodes.Select(code => code.ToString()));
            outcomes.Add(HttpActivityOutcomes.UnmatchedStatusCode);
        }

        return outcomes.ToArray();
    }

    internal static RuntimeInputBinding LiteralBinding(string inputName, object value, Type type)
    {
        var valueType = ValueType(type);
        return
        new(
            inputKey: inputName,
            targetType: valueType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                valueType,
                JsonSerializer.SerializeToElement(value, type),
                ValueProtectionPolicy.InstanceInline));
    }

    internal static ValueTypeDescriptor ValueType(Type type) =>
        TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias) is { } reference
            ? new ValueTypeDescriptor(reference.Alias, reference.CollectionKind)
            : throw new InvalidOperationException($"Could not describe '{type}'.");

    internal static Elsa.Serialization.Core.IPayloadSerializer Serializer =>
        TestPayloadSerializers.NewPayloadSerializer();

    internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
