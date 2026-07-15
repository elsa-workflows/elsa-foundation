using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

public sealed class ActivityDefinitionAuthoringApiTests
{
    private const string Root = "Elsa.Activities.Design.Api.Endpoints";

    public static TheoryData<string, string, string> Routes => new()
    {
        { "Definitions.Add", "POST", "design/activities/definitions" },
        { "Definitions.Fork", "POST", "design/activities/definitions/{definitionId}/forks" },
        { "Definitions.List", "GET", "design/activities/definitions" },
        { "Definitions.Get", "GET", "design/activities/definitions/{definitionId}" },
        { "Definitions.AddDraft", "POST", "design/activities/definitions/{definitionId}/drafts" },
        { "Definitions.ListDrafts", "GET", "design/activities/definitions/{definitionId}/drafts" },
        { "Definitions.ListVersions", "GET", "design/activities/definitions/{definitionId}/versions" },
        { "Drafts.Get", "GET", "design/activities/drafts/{draftId}" },
        { "Drafts.Replace", "PUT", "design/activities/drafts/{draftId}" },
        { "Drafts.Discard", "DELETE", "design/activities/drafts/{draftId}" },
        { "Drafts.Validate", "POST", "design/activities/drafts/{draftId}/validate" },
        { "Drafts.Diff", "POST", "design/activities/drafts/{draftId}/diff" },
        { "Versions.Diff", "GET", "design/activities/versions/{fromVersionId}/diff/{toVersionId}" },
        { "Versions.Dependencies", "GET", "design/activities/versions/{versionId}/dependencies" },
        { "Versions.Get", "GET", "design/activities/versions/{versionId}" }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void Endpoint_routes_match_the_reviewed_wire_contract(string endpointName, string verb, string route)
    {
        var definition = ConfiguredDefinition($"{Root}.{endpointName}");

        Assert.Equal(verb, Assert.Single(definition.Verbs));
        Assert.Equal(route, Assert.Single(definition.Routes));
    }

    [Fact]
    public void Create_request_uses_the_contract_shape_and_accepts_opaque_provider_payload()
    {
        var request = new CreateReusableActivityDefinition(
            "acme.orders.calculate-total",
            "Orders",
            "Calculate order total",
            null,
            new("elsa.activity-graph", "1", Json("{\"secret\":42}")),
            new("1", [], [], [new("done", "Done", true)]),
            []);

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"activityTypeKey\":\"acme.orders.calculate-total\"", json, StringComparison.Ordinal);
        Assert.Contains("\"providerKey\":\"elsa.activity-graph\"", json, StringComparison.Ordinal);
        Assert.Contains("\"payload\":{\"secret\":42}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("tenantId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unauthorized_provider_projection_omits_payload_instead_of_leaking_or_fabricating_it()
    {
        var view = new ActivityProviderManifestView("elsa.activity-graph", "1", null);

        var json = JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("{\"providerKey\":\"elsa.activity-graph\",\"schemaVersion\":\"1\"}", json);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Single")]
    public void Scalar_type_reference_input_accepts_wire_name_and_single_alias(string collectionKind)
    {
        var json = $$"""
            {
              "contractSchemaVersion": "1",
              "inputs": [{
                "referenceKey": "order",
                "name": "Order",
                "type": { "alias": "acme.order", "collectionKind": "{{collectionKind}}" },
                "isRequired": true,
                "default": null,
                "storageDriverKey": "elsa.json"
              }],
              "outputs": [],
              "outcomes": []
            }
            """;

        var view = JsonSerializer.Deserialize<ActivityContractView>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(CollectionKind.Single, Assert.Single(view!.ToDomain().Inputs).Type.CollectionKind);
    }

    [Fact]
    public void Scalar_type_reference_response_always_emits_canonical_none_wire_name()
    {
        var domain = new ActivityContract(
            "1",
            [new("order", "Order", new("acme.order", CollectionKind.Single), true, null, "elsa.json")],
            [],
            []);

        var json = JsonSerializer.Serialize(domain.ToView(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"collectionKind\":\"None\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"collectionKind\":\"Single\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Problem_details_exposes_stable_error_code_and_safe_diagnostics_shape()
    {
        var problem = new ActivityProblemDetailsView(
            "https://elsa.dev/problems/activity-draft-stale-revision",
            "Activity draft revision is stale",
            409,
            "The draft changed after the submitted revision was read.",
            "/design/activities/drafts/draft-1",
            "activity.draft.stale-revision",
            "trace-1",
            []);

        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"errorCode\":\"activity.draft.stale-revision\"", json, StringComparison.Ordinal);
        Assert.Contains("\"diagnostics\":[]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("providerManifest", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Diff_view_serializes_the_reviewed_string_classification_shape()
    {
        var domain = new ActivityVersionDiff(
            new("ActivityVersion", "definition-1", VersionId: "version-1", Version: "1.0.0", TemplateHash: "sha256-before"),
            new("ActivityDraft", "definition-1", DraftId: "draft-1", Revision: 3),
            ActivityVersionCompatibility.Breaking,
            ActivityVersionBump.Major,
            true,
            new("elsa.activity-graph", "1", "elsa.activity-graph", "2", true),
            new(1, 0, 0, 0),
            [new(
                "contract:input:value:type-changed",
                ActivityVersionChangeArea.Contract,
                "TypeChanged",
                new("Input", "value"),
                null,
                null,
                ActivityVersionChangeImpact.Breaking,
                ActivityVersionBump.Major,
                "Input changed type.")],
            []);

        var json = JsonSerializer.Serialize(domain.ToView(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"compatibility\":\"Breaking\"", json, StringComparison.Ordinal);
        Assert.Contains("\"requiredBump\":\"Major\"", json, StringComparison.Ordinal);
        Assert.Contains("\"area\":\"Contract\"", json, StringComparison.Ordinal);
        Assert.Contains("\"impact\":\"Breaking\"", json, StringComparison.Ordinal);
    }

    private static EndpointDefinition ConfiguredDefinition(string endpointTypeName)
    {
        var endpointType = typeof(CreateReusableActivityDefinition).Assembly.GetType(endpointTypeName, throwOnError: true)!;
        var dependencies = endpointType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(x => ResolveDependency(x.ParameterType))
            .ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(x => x.Name == nameof(Factory.Create)
                         && x.IsGenericMethodDefinition
                         && x.GetParameters() is [var first, var second]
                         && first.ParameterType == typeof(Action<DefaultHttpContext>)
                         && second.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        var endpoint = (BaseEndpoint)create.Invoke(null, [(Action<DefaultHttpContext>)(_ => { }), dependencies])!;
        endpoint.Configure();
        return endpoint.Definition;
    }

    private static object ResolveDependency(Type type)
    {
        if (type == typeof(IRequestSender)) return new StubRequestSender();
        if (type == typeof(ICommandSender)) return new StubCommandSender();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>))
        {
            var loggerType = typeof(NullLogger<>).MakeGenericType(type.GetGenericArguments()[0]);
            return (loggerType.GetProperty("Instance")?.GetValue(null)
                    ?? loggerType.GetField("Instance")?.GetValue(null))!;
        }
        throw new InvalidOperationException($"Unexpected endpoint dependency '{type}'.");
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class StubRequestSender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            throw new InvalidOperationException("Configuration-only test.");
    }

    private sealed class StubCommandSender : ICommandSender
    {
        public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull =>
            throw new InvalidOperationException("Configuration-only test.");
        public Task Send(Elsa.Mediator.Core.Contracts.ICommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Configuration-only test.");
    }
}
