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
        { "Definitions.PreviewFork", "POST", "design/activities/definitions/{definitionId}/fork-previews" },
        { "Forks.Apply", "POST", "design/activities/fork-candidates/{candidateId}/apply" },
        { "Forks.GetStatus", "GET", "design/activities/forks/{idempotencyKey}" },
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
        { "Drafts.UpdatePresentation", "PATCH", "design/activities/drafts/{draftId}/presentation" },
        { "Drafts.ConflictCopy", "POST", "design/activities/drafts/{draftId}/conflict-copies" },
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
        { "UpgradePlans.Apply", "POST", "design/activities/upgrade-plans/{planId}/apply" },
        { "UpgradePlans.GetReceipt", "GET", "design/activities/upgrade-plans/{planId}/receipts/{receiptId}" },
        { "UpgradePlans.Refresh", "POST", "design/activities/upgrade-plans/{planId}/refresh" }
    };

    [Fact]
    public void Activity_design_capability_advertises_management_authoring_picker_and_recommendation_relations()
    {
        var links = Elsa.Activities.Design.Api.Capabilities.ActivityDesignApiCapabilities.StaticDeclaration.Links;

        Assert.Contains(links, x => x.Rel == "recommended-activity-definitions" && x.Href == "design/activities/definitions/picker" && !x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definition-recommendation" && x.Href == "design/activities/definitions/{definitionId}/recommendation" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-authoring-capabilities" && x.Href == "design/activities/authoring-capabilities" && !x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definitions" && x.Href == "design/activities/definitions" && !x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definition" && x.Href == "design/activities/definitions/{definitionId}" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definition-drafts" && x.Href == "design/activities/definitions/{definitionId}/drafts" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definition-draft" && x.Href == "design/activities/drafts/{draftId}" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-draft-validation" && x.Href == "design/activities/drafts/{draftId}/validate" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-draft-contract-proposals" && x.Href == "design/activities/drafts/{draftId}/contract-proposals" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-draft-contract-proposals-apply" && x.Href == "design/activities/drafts/{draftId}/contract-proposals/apply" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definition-versions" && x.Href == "design/activities/definitions/{definitionId}/versions" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definition-version" && x.Href == "design/activities/versions/{versionId}" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-version-diff" && x.Href == "design/activities/versions/{fromVersionId}/diff/{toVersionId}" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definition-fork-preview" && x.Href == "design/activities/definitions/{definitionId}/fork-previews" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definition-fork-apply" && x.Href == "design/activities/fork-candidates/{candidateId}/apply" && x.Templated);
        Assert.Contains(links, x => x.Rel == "activity-definition-fork-status" && x.Href == "design/activities/forks/{idempotencyKey}" && x.Templated);
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
            new PreviewReusableActivityFork("definition-route", "preview-operation", "source-version", "Category", "Display", null, "provider", "1"),
            new ApplyReusableActivityFork("candidate-route", "sha256:request", "fork-operation"),
            new CreateReusableActivityDraft("definition-route", null),
            new ReplaceReusableActivityDraft(
                "draft-route",
                3,
                new("1", [], [], []),
                new("provider", "1", Json("{}")),
                []),
            new UpdateReusableActivityDraftPresentation("draft-route", 3, "Review candidate"),
            new CreateReusableActivityDraftConflictCopy(
                "draft-route",
                3,
                new("1", [], [], []),
                new("provider", "1", Json("{}")),
                [],
                "Recovered local work"),
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
            new ApplyActivityUpgradePlan("plan-route", "stage-1", "operation-1")
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
            "{\"stageId\":\"stage-1\",\"idempotencyKey\":\"operation-1\"}",
            JsonSerializer.Serialize(new ApplyActivityUpgradePlan("plan-route", "stage-1", "operation-1"), options));
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

    [Theory]
    [InlineData(typeof(PreviewReusableActivityFork), nameof(PreviewReusableActivityFork.DefinitionId))]
    [InlineData(typeof(ApplyReusableActivityFork), nameof(ApplyReusableActivityFork.CandidateId))]
    [InlineData(typeof(CreateReusableActivityDraft), nameof(CreateReusableActivityDraft.DefinitionId))]
    [InlineData(typeof(UpdateReusableActivityDefinition), nameof(UpdateReusableActivityDefinition.DefinitionId))]
    [InlineData(typeof(ReplaceReusableActivityDraft), nameof(ReplaceReusableActivityDraft.DraftId))]
    [InlineData(typeof(UpdateReusableActivityDraftPresentation), nameof(UpdateReusableActivityDraftPresentation.DraftId))]
    [InlineData(typeof(CreateReusableActivityDraftConflictCopy), nameof(CreateReusableActivityDraftConflictCopy.DraftId))]
    [InlineData(typeof(DiscardReusableActivityDraft), nameof(DiscardReusableActivityDraft.DraftId))]
    [InlineData(typeof(ValidateReusableActivityDraft), nameof(ValidateReusableActivityDraft.DraftId))]
    [InlineData(typeof(MigrateReusableActivityDraft), nameof(MigrateReusableActivityDraft.DraftId))]
    [InlineData(typeof(ProposeReusableActivityContract), nameof(ProposeReusableActivityContract.DraftId))]
    [InlineData(typeof(ApplyReusableActivityContractProposal), nameof(ApplyReusableActivityContractProposal.DraftId))]
    [InlineData(typeof(RetireReusableActivityVersion), nameof(RetireReusableActivityVersion.VersionId))]
    [InlineData(typeof(RestoreReusableActivityVersion), nameof(RestoreReusableActivityVersion.VersionId))]
    [InlineData(typeof(RevokeReusableActivityVersion), nameof(RevokeReusableActivityVersion.VersionId))]
    [InlineData(typeof(SetRecommendedReusableActivityVersion), nameof(SetRecommendedReusableActivityVersion.DefinitionId))]
    [InlineData(typeof(PreviewActivityDraftDiff), nameof(PreviewActivityDraftDiff.DraftId))]
    [InlineData(typeof(ApplyActivityUpgradePlan), nameof(ApplyActivityUpgradePlan.PlanId))]
    [InlineData(typeof(RefreshActivityUpgradePlan), nameof(RefreshActivityUpgradePlan.PlanId))]
    public void Mutating_route_identifiers_are_explicitly_bound_from_the_route(Type requestType, string propertyName)
    {
        var property = requestType.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RouteParamAttribute>());
        Assert.NotNull(property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>());
    }

    public static TheoryData<string, string, string, object> RouteBoundDispatches => new()
    {
        {
            "Drafts.Replace",
            "draftId",
            nameof(ReplaceReusableActivityDraft.DraftId),
            new ReplaceReusableActivityDraft(
                null!,
                3,
                new("1", [], [], []),
                new("provider", "1", Json("{}")),
                [])
        },
        {
            "Drafts.ProposeContract",
            "draftId",
            nameof(ProposeReusableActivityContract.DraftId),
            new ProposeReusableActivityContract(null!, 3, "provider", "1", "sha256:manifest")
        },
        {
            "Drafts.Discard",
            "draftId",
            nameof(DiscardReusableActivityDraft.DraftId),
            new DiscardReusableActivityDraft(null!, 3)
        }
    };

    [Theory]
    [MemberData(nameof(RouteBoundDispatches))]
    public async Task Route_identifiers_are_injected_before_mediator_dispatch(
        string endpointName,
        string routeParameterName,
        string propertyName,
        object request)
    {
        const string routeValue = "draft-from-route";
        var sender = new CapturingMediatorSender();
        var endpoint = CreateEndpoint(
            $"{Root}.{endpointName}",
            context => context.Request.RouteValues[routeParameterName] = routeValue,
            sender);
        var handle = endpoint.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == "HandleAsync"
                              && method.GetParameters() is [var first, var second]
                              && first.ParameterType == request.GetType()
                              && second.ParameterType == typeof(CancellationToken));

        var invocation = (Task)handle.Invoke(endpoint, [request, CancellationToken.None])!;

        await Assert.ThrowsAsync<OperationCanceledException>(() => invocation);
        Assert.NotNull(sender.Message);
        Assert.Equal(routeValue, sender.Message!.GetType().GetProperty(propertyName)!.GetValue(sender.Message));
    }

    [Fact]
    public void Create_request_accepts_an_optional_activity_type_key_and_opaque_provider_payload()
    {
        var request = new CreateReusableActivityDefinition(
            "Orders",
            "Calculate order total",
            null,
            new("elsa.activity-graph", "1", Json("{\"secret\":42}")),
            new("1", [], [], [new("done", "Done", true)]),
            [],
            "elsa.user.calculate-order-total.custom");

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"activityTypeKey\":\"elsa.user.calculate-order-total.custom\"", json, StringComparison.Ordinal);
        Assert.Contains("\"providerKey\":\"elsa.activity-graph\"", json, StringComparison.Ordinal);
        Assert.Contains("\"payload\":{\"secret\":42}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("tenantId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_request_omits_activity_type_key_when_server_generation_is_requested()
    {
        var request = new CreateReusableActivityDefinition(
            "Orders",
            "Calculate order total",
            null,
            new("elsa.activity-graph", "1", Json("{}")),
            new("1", [], [], []),
            []);

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("activityTypeKey", json, StringComparison.OrdinalIgnoreCase);
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
                "isNullable": true,
                "default": null,
                "storageDriverKey": "elsa.json"
              }],
              "outputs": [],
              "outcomes": []
            }
            """;

        var view = JsonSerializer.Deserialize<ActivityContractView>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var input = Assert.Single(view!.ToDomain().Inputs);
        Assert.Equal(CollectionKind.Single, input.Type.CollectionKind);
        Assert.True(input.IsNullable);
    }

    [Fact]
    public void Mutable_contract_wire_shape_requires_explicit_nullability()
    {
        const string json = """
            {
              "contractSchemaVersion": "1",
              "inputs": [{
                "referenceKey": "order",
                "name": "Order",
                "type": { "alias": "acme.order", "collectionKind": "Single" },
                "isRequired": true,
                "default": null,
                "storageDriverKey": "elsa.json"
              }],
              "outputs": [],
              "outcomes": []
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ActivityContractView>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Scalar_type_reference_response_always_emits_canonical_single_wire_name()
    {
        var domain = new ActivityContract(
            "1",
            [new("order", "Order", new("acme.order", CollectionKind.Single), true, false, null, "elsa.json")],
            [],
            []);

        var json = JsonSerializer.Serialize(domain.ToView(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"collectionKind\":\"Single\"", json, StringComparison.Ordinal);
        Assert.Contains("\"isNullable\":false", json, StringComparison.Ordinal);
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
    public void Problem_details_projects_typed_conflict_copy_recovery_without_internal_state()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        context.Request.Path = "/design/activities/drafts/draft-1";
        var problem = ActivityProblemDetails.From(new ActivityAuthoringException(
            409,
            "activity.draft.stale-revision",
            "Activity draft revision is stale",
            "The draft changed after the submitted revision was read.",
            recovery: new(
                8,
                "activity-draft-conflict-copies",
                "design/activities/drafts/draft-1/conflict-copies",
                "review-current-revision-and-create-conflict-copy")), context);

        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"currentRevision\":8", json, StringComparison.Ordinal);
        Assert.Contains("\"relation\":\"activity-draft-conflict-copies\"", json, StringComparison.Ordinal);
        Assert.Contains("\"instruction\":\"review-current-revision-and-create-conflict-copy\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("provider", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("layout", json, StringComparison.OrdinalIgnoreCase);
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
        var endpoint = CreateEndpoint(endpointTypeName, _ => { });
        endpoint.Configure();
        return endpoint.Definition;
    }

    private static BaseEndpoint CreateEndpoint(
        string endpointTypeName,
        Action<DefaultHttpContext> configureContext,
        CapturingMediatorSender? sender = null)
    {
        var endpointType = typeof(CreateReusableActivityDefinition).Assembly.GetType(endpointTypeName, throwOnError: true)!;
        var dependencies = endpointType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(x => ResolveDependency(x.ParameterType, sender))
            .ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(x => x.Name == nameof(Factory.Create)
                         && x.IsGenericMethodDefinition
                         && x.GetParameters() is [var first, var second]
                         && first.ParameterType == typeof(Action<DefaultHttpContext>)
                         && second.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        return (BaseEndpoint)create.Invoke(null, [configureContext, dependencies])!;
    }

    private static object ResolveDependency(Type type, CapturingMediatorSender? sender)
    {
        if (type == typeof(IRequestSender)) return sender is null ? new StubRequestSender() : sender;
        if (type == typeof(ICommandSender)) return sender is null ? new StubCommandSender() : sender;
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

    private sealed class CapturingMediatorSender : ICommandSender, IRequestSender
    {
        public object? Message { get; private set; }

        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            Message = request;
            return Task.FromException<T>(new OperationCanceledException());
        }

        public Task<T> Send<T>(
            Elsa.Mediator.Core.Contracts.ICommand<T> command,
            CancellationToken cancellationToken = default) where T : notnull
        {
            Message = command;
            return Task.FromException<T>(new OperationCanceledException());
        }

        public Task Send(
            Elsa.Mediator.Core.Contracts.ICommand command,
            CancellationToken cancellationToken = default)
        {
            Message = command;
            return Task.FromException(new OperationCanceledException());
        }
    }
}
