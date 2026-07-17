using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
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
        { "AuthoringCapabilities.Get", "GET", "design/activities/authoring-capabilities" },
        { "Definitions.Add", "POST", "design/activities/definitions" },
        { "Definitions.Fork", "POST", "design/activities/definitions/{definitionId}/forks" },
        { "Definitions.List", "GET", "design/activities/definitions" },
        { "Definitions.Get", "GET", "design/activities/definitions/{definitionId}" },
        { "Definitions.Update", "PATCH", "design/activities/definitions/{definitionId}" },
        { "Definitions.Recommendation", "PUT", "design/activities/definitions/{definitionId}/recommendation" },
        { "Definitions.Picker", "GET", "design/activities/definitions/picker" },
        { "Definitions.AddDraft", "POST", "design/activities/definitions/{definitionId}/drafts" },
        { "Definitions.ListDrafts", "GET", "design/activities/definitions/{definitionId}/drafts" },
        { "Definitions.ListVersions", "GET", "design/activities/definitions/{definitionId}/versions" },
        { "Drafts.Get", "GET", "design/activities/drafts/{draftId}" },
        { "Drafts.Replace", "PUT", "design/activities/drafts/{draftId}" },
        { "Drafts.Discard", "DELETE", "design/activities/drafts/{draftId}" },
        { "Drafts.Validate", "POST", "design/activities/drafts/{draftId}/validate" },
        { "Drafts.MigrateProvider", "POST", "design/activities/drafts/{draftId}/migrate-provider" },
        { "Drafts.ProposeContract", "POST", "design/activities/drafts/{draftId}/contract-proposals" },
        { "Drafts.ApplyContractProposal", "POST", "design/activities/drafts/{draftId}/contract-proposals/apply" },
        { "Drafts.Diff", "POST", "design/activities/drafts/{draftId}/diff" },
        { "Versions.Diff", "GET", "design/activities/versions/{fromVersionId}/diff/{toVersionId}" },
        { "Versions.Dependencies", "GET", "design/activities/versions/{versionId}/dependencies" },
        { "Versions.Get", "GET", "design/activities/versions/{versionId}" },
        { "Versions.Retire", "POST", "design/activities/versions/{versionId}/retire" },
        { "Versions.Restore", "POST", "design/activities/versions/{versionId}/restore" },
        { "Versions.Revoke", "POST", "design/activities/versions/{versionId}/revoke" },
        { "UpgradePlans.Create", "POST", "design/activities/upgrade-plans" },
        { "UpgradePlans.Get", "GET", "design/activities/upgrade-plans/{planId}" },
        { "UpgradePlans.Apply", "POST", "design/activities/upgrade-plans/{planId}/apply" }
    };

    [Fact]
    public void Activity_design_capability_advertises_authoring_picker_and_templated_recommendation_relations()
    {
        var links = Elsa.Activities.Design.Api.Capabilities.ActivityDesignApiCapabilities.StaticDeclaration.Links;

        Assert.Contains(links, x => x.Rel == "recommended-activity-definitions" && x.Href == "design/activities/definitions/picker" && !x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definition-recommendation" && x.Href == "design/activities/definitions/{definitionId}/recommendation" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-authoring-capabilities" && x.Href == "design/activities/authoring-capabilities" && !x.Templated);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void Endpoint_routes_match_the_reviewed_wire_contract(string endpointName, string verb, string route)
    {
        var definition = ConfiguredDefinition($"{Root}.{endpointName}");

        Assert.Equal(verb, Assert.Single(definition.Verbs));
        Assert.Equal(route, Assert.Single(definition.Routes));
    }

    [Fact]
    public void Route_bound_identifiers_are_not_part_of_mutating_request_bodies()
    {
        object[] requests =
        [
            new ForkReusableActivityDefinition("definition-route", "source-version", "Category", "Display", null, "provider", "1"),
            new CreateReusableActivityDraft("definition-route", null),
            new ReplaceReusableActivityDraft(
                "draft-route",
                3,
                new("1", [], [], []),
                new("provider", "1", Json("{}")),
                []),
            new DiscardReusableActivityDraft("draft-route", 3),
            new ValidateReusableActivityDraft("draft-route", 3),
            new MigrateReusableActivityDraft("draft-route", 3, "provider", "2"),
            new ProposeReusableActivityContract("draft-route", 3, "provider", "1", "sha256:manifest"),
            new ApplyReusableActivityContractProposal("draft-route", 3, "provider", "1", "sha256:manifest", "sha256:proposal", ["change-1"]),
            new RetireReusableActivityVersion("version-route", ActivityDefinitionVersionLifecycle.Active, "reason"),
            new RestoreReusableActivityVersion("version-route", ActivityDefinitionVersionLifecycle.Retired, "reason"),
            new RevokeReusableActivityVersion("version-route", ActivityDefinitionVersionLifecycle.Active, "reason"),
            new SetRecommendedReusableActivityVersion(
                "definition-route",
                "version-head",
                "version-current",
                "version-target",
                ActivityDefinitionVersionLifecycle.Active,
                "reason"),
            new PreviewActivityDraftDiff("draft-route", 3, "base-version"),
            new ApplyActivityUpgradePlan("plan-route", ["step-1"])
        ];

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        foreach (var request in requests)
        {
            var json = JsonSerializer.Serialize(request, request.GetType(), options);
            Assert.DoesNotContain("definition-route", json, StringComparison.Ordinal);
            Assert.DoesNotContain("draft-route", json, StringComparison.Ordinal);
            Assert.DoesNotContain("version-route", json, StringComparison.Ordinal);
            Assert.DoesNotContain("plan-route", json, StringComparison.Ordinal);
        }

        Assert.Equal(
            "{\"expectedRevision\":3,\"baseVersionId\":\"base-version\"}",
            JsonSerializer.Serialize(new PreviewActivityDraftDiff("draft-route", 3, "base-version"), options));
        Assert.Equal(
            "{\"selectedStepIds\":[\"step-1\"]}",
            JsonSerializer.Serialize(new ApplyActivityUpgradePlan("plan-route", ["step-1"]), options));
        Assert.Equal(
            "{\"expectedDefinitionHeadVersionId\":\"version-head\",\"expectedRecommendedVersionId\":\"version-current\",\"recommendedVersionId\":\"version-target\",\"expectedRecommendedVersionLifecycle\":\"Active\",\"reason\":\"reason\"}",
            JsonSerializer.Serialize(new SetRecommendedReusableActivityVersion(
                "definition-route",
                "version-head",
                "version-current",
                "version-target",
                ActivityDefinitionVersionLifecycle.Active,
                "reason"), options));
    }

    [Fact]
    public void Create_request_uses_the_contract_shape_and_accepts_opaque_provider_payload()
    {
        var request = new CreateReusableActivityDefinition(
            "Orders",
            "Calculate order total",
            null,
            new("elsa.activity-graph", "1", Json("{\"secret\":42}")),
            new("1", [], [], [new("done", "Done", true)]),
            []);

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("activityTypeKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"providerKey\":\"elsa.activity-graph\"", json, StringComparison.Ordinal);
        Assert.Contains("\"payload\":{\"secret\":42}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("tenantId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_request_body_is_exactly_the_documented_presentation_metadata_shape()
    {
        var request = new UpdateReusableActivityDefinition(
            "activity-def-order-total",
            "Finance",
            "Calculate invoice total",
            "Updated description");

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("{\"category\":\"Finance\",\"displayName\":\"Calculate invoice total\",\"description\":\"Updated description\"}", json);
        Assert.DoesNotContain("definitionId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("activityTypeKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unauthorized_provider_projection_omits_payload_instead_of_leaking_or_fabricating_it()
    {
        var view = new ActivityProviderManifestView("elsa.activity-graph", "1", "sha256:test", null);

        var json = JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("{\"providerKey\":\"elsa.activity-graph\",\"schemaVersion\":\"1\",\"manifestFingerprint\":\"sha256:test\"}", json);
    }

    [Theory]
    [InlineData("Single")]
    public void Scalar_type_reference_input_accepts_canonical_wire_name(string collectionKind)
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
    public void Scalar_type_reference_response_always_emits_canonical_single_wire_name()
    {
        var domain = new ActivityContract(
            "1",
            [new("order", "Order", new("acme.order", CollectionKind.Single), true, null, "elsa.json")],
            [],
            []);

        var json = JsonSerializer.Serialize(domain.ToView(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"collectionKind\":\"Single\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"collectionKind\":\"None\"", json, StringComparison.Ordinal);
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
    public void Problem_details_never_exposes_internal_exception_messages()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/design/activities/dependencies";
        context.TraceIdentifier = "trace-1";
        var exception = new ActivityAuthoringException(
            StatusCodes.Status500InternalServerError,
            "activity.dependency.query-failed",
            "Dependency query failed",
            "Database connection string and provider details must remain private.");

        var problem = ActivityProblemDetails.From(exception, context);

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("activity.operation.failed", problem.ErrorCode);
        Assert.Equal("The activity operation failed.", problem.Detail);
        Assert.DoesNotContain("connection string", JsonSerializer.Serialize(problem), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Diagnostics_serialize_empty_metadata_as_an_object_and_severity_as_a_string()
    {
        var diagnostic = new ActivityDiagnostic(
            "activity.contract.invalid",
            ActivityDiagnosticSeverity.Error,
            "The contract is invalid.",
            new("ActivityDraft", "draft-1"));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        var json = JsonSerializer.Serialize(diagnostic, options);

        Assert.Contains("\"severity\":\"Error\"", json, StringComparison.Ordinal);
        Assert.Contains("\"metadata\":{}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"metadata\":null", json, StringComparison.Ordinal);
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
