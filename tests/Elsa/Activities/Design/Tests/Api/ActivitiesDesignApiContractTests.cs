using Elsa.Activities.Design.Tests.Api.Support;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

/// <summary>
/// Post-migration real-host contract gate. T019 supplies the production mapper; until then the
/// reflection seam in <see cref="ActivitiesDesignMinimalApiHost"/> fails explicitly and keeps this
/// test-first contract compiling against the pre-migration owner.
/// </summary>
public sealed class ActivitiesDesignApiContractTests
{
    private const string OwnerId = "Elsa.Activities.Design.Api";
    private const string OperationPrefix = "ElsaActivitiesDesignApiEndpoints";

    private static readonly IReadOnlyDictionary<string, string?> RequestTypes = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Availability.GetSettings"] = "Elsa.Activities.Design.Api.Requests.GetActivityAvailabilitySettings",
        ["Availability.ListDiagnostics"] = null,
        ["Availability.SaveSettings"] = "Elsa.Activities.Design.Api.Commands.SaveActivityAvailabilitySettings",
        ["AuthoringCapabilities.Get"] = null,
        ["Catalog.List"] = "Elsa.Activities.Design.Api.Requests.ListActivityAuthoringCatalog",
        ["Definitions.Add"] = "Elsa.Activities.Design.Api.Commands.CreateReusableActivityDefinition",
        ["Definitions.PreviewFork"] = "Elsa.Activities.Design.Api.Commands.PreviewReusableActivityFork",
        ["Definitions.List"] = "Elsa.Activities.Design.Api.Commands.ListReusableActivityDefinitions",
        ["Definitions.Get"] = "Elsa.Activities.Design.Api.Commands.GetReusableActivityDefinition",
        ["Definitions.Update"] = "Elsa.Activities.Design.Api.Commands.UpdateReusableActivityDefinition",
        ["Definitions.Recommendation"] = "Elsa.Activities.Design.Api.Commands.SetRecommendedReusableActivityVersion",
        ["Definitions.Picker"] = "Elsa.Activities.Design.Api.Requests.ListRecommendedActivityDefinitions",
        ["Definitions.ListDrafts"] = "Elsa.Activities.Design.Api.Commands.ListReusableActivityDrafts",
        ["Definitions.AddDraft"] = "Elsa.Activities.Design.Api.Commands.CreateReusableActivityDraft",
        ["Definitions.ListVersions"] = "Elsa.Activities.Design.Api.Commands.ListReusableActivityVersions",
        ["Drafts.Get"] = "Elsa.Activities.Design.Api.Commands.GetReusableActivityDraft",
        ["Drafts.Replace"] = "Elsa.Activities.Design.Api.Commands.ReplaceReusableActivityDraft",
        ["Drafts.UpdatePresentation"] = "Elsa.Activities.Design.Api.Commands.UpdateReusableActivityDraftPresentation",
        ["Drafts.ConflictCopy"] = "Elsa.Activities.Design.Api.Commands.CreateReusableActivityDraftConflictCopy",
        ["Drafts.Validate"] = "Elsa.Activities.Design.Api.Commands.ValidateReusableActivityDraft",
        ["Drafts.MigrateProvider"] = "Elsa.Activities.Design.Api.Commands.MigrateReusableActivityDraft",
        ["Drafts.ProposeContract"] = "Elsa.Activities.Design.Api.Commands.ProposeReusableActivityContract",
        ["Drafts.ApplyContractProposal"] = "Elsa.Activities.Design.Api.Commands.ApplyReusableActivityContractProposal",
        ["Drafts.Discard"] = "Elsa.Activities.Design.Api.Commands.DiscardReusableActivityDraft",
        ["Drafts.Diff"] = "Elsa.Activities.Design.Api.Requests.PreviewActivityDraftDiff",
        ["Forks.Apply"] = "Elsa.Activities.Design.Api.Commands.ApplyReusableActivityFork",
        ["Forks.GetStatus"] = "Elsa.Activities.Design.Api.Commands.GetReusableActivityForkStatus",
        ["Versions.Dependencies"] = "Elsa.Activities.Design.Api.Requests.GetActivityDependencies",
        ["Versions.Diff"] = "Elsa.Activities.Design.Api.Requests.CompareActivityVersions",
        ["Versions.Get"] = "Elsa.Activities.Design.Api.Commands.GetReusableActivityVersion",
        ["Versions.Retire"] = "Elsa.Activities.Design.Api.Commands.RetireReusableActivityVersion",
        ["Versions.Restore"] = "Elsa.Activities.Design.Api.Commands.RestoreReusableActivityVersion",
        ["Versions.Revoke"] = "Elsa.Activities.Design.Api.Commands.RevokeReusableActivityVersion",
        ["UpgradePlans.Create"] = "Elsa.Activities.Design.Api.Commands.CreateActivityUpgradePlan",
        ["UpgradePlans.Get"] = "Elsa.Activities.Design.Api.Requests.GetActivityUpgradePlan",
        ["UpgradePlans.Apply"] = "Elsa.Activities.Design.Api.Commands.ApplyActivityUpgradePlan",
        ["UpgradePlans.GetReceipt"] = "Elsa.Activities.Design.Api.Requests.GetActivityUpgradeApplyReceipt",
        ["UpgradePlans.Refresh"] = "Elsa.Activities.Design.Api.Commands.RefreshActivityUpgradePlan"
    };

    private static readonly IReadOnlyDictionary<string, string> ResponseTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Availability.GetSettings"] = "Elsa.Activities.Design.Core.Models.ActivityAvailabilitySettings",
        ["Availability.ListDiagnostics"] = "Elsa.Activities.Design.Core.Models.ActivityAvailabilityDiagnostics",
        ["Availability.SaveSettings"] = "Elsa.Activities.Design.Core.Models.ActivityAvailabilitySettings",
        ["AuthoringCapabilities.Get"] = "Elsa.Activities.Design.Api.Models.ActivityAuthoringCapabilitiesView",
        ["Catalog.List"] = "Elsa.Activities.Design.Api.Models.ActivityAuthoringCatalogView",
        ["Definitions.Add"] = "Elsa.Activities.Design.Api.Models.ReusableActivityDefinitionMutationView",
        ["Definitions.PreviewFork"] = "Elsa.Activities.Design.Api.Models.ActivityForkPreviewView",
        ["Definitions.List"] = "Elsa.Activities.Design.Api.Models.ActivityManagementPageView`1",
        ["Definitions.Get"] = "Elsa.Activities.Design.Api.Models.ReusableActivityDefinitionManagementView",
        ["Definitions.Update"] = "Elsa.Activities.Design.Api.Models.ActivityDefinitionIdentityView",
        ["Definitions.Recommendation"] = "Elsa.Activities.Design.Api.Models.ActivityDefinitionRecommendationView",
        ["Definitions.Picker"] = "Elsa.Activities.Design.Api.Models.RecommendedActivityDefinitionPageView",
        ["Definitions.ListDrafts"] = "Elsa.Activities.Design.Api.Models.ActivityManagementPageView`1",
        ["Definitions.AddDraft"] = "Elsa.Activities.Design.Api.Models.ReusableActivityDraftView",
        ["Definitions.ListVersions"] = "Elsa.Activities.Design.Api.Models.ActivityManagementPageView`1",
        ["Drafts.Get"] = "Elsa.Activities.Design.Api.Models.ReusableActivityDraftView",
        ["Drafts.Replace"] = "Elsa.Activities.Design.Api.Models.ReusableActivityDraftView",
        ["Drafts.UpdatePresentation"] = "Elsa.Activities.Design.Api.Models.ReusableActivityDraftView",
        ["Drafts.ConflictCopy"] = "Elsa.Activities.Design.Api.Models.ReusableActivityDraftView",
        ["Drafts.Validate"] = "Elsa.Activities.Design.Api.Models.ActivityDraftValidationView",
        ["Drafts.MigrateProvider"] = "Elsa.Activities.Design.Api.Models.ReusableActivityDraftView",
        ["Drafts.ProposeContract"] = "Elsa.Activities.Design.Api.Models.ActivityContractProposalView",
        ["Drafts.ApplyContractProposal"] = "Elsa.Activities.Design.Api.Models.ReusableActivityDraftView",
        ["Drafts.Discard"] = "System.Void",
        ["Drafts.Diff"] = "Elsa.Activities.Design.Api.Models.ActivityVersionDiffView",
        ["Forks.Apply"] = "Elsa.Activities.Design.Api.Models.ActivityForkReceiptView",
        ["Forks.GetStatus"] = "Elsa.Activities.Design.Api.Models.ActivityForkReceiptView",
        ["Versions.Dependencies"] = "Elsa.Activities.Design.Api.Models.ActivityDependencyPageView",
        ["Versions.Diff"] = "Elsa.Activities.Design.Api.Models.ActivityVersionDiffView",
        ["Versions.Get"] = "Elsa.Activities.Design.Api.Models.ReusableActivityVersionView",
        ["Versions.Retire"] = "Elsa.Activities.Design.Api.Models.ReusableActivityVersionLifecycleView",
        ["Versions.Restore"] = "Elsa.Activities.Design.Api.Models.ReusableActivityVersionLifecycleView",
        ["Versions.Revoke"] = "Elsa.Activities.Design.Api.Models.ReusableActivityVersionLifecycleView",
        ["UpgradePlans.Create"] = "Elsa.Activities.Design.Api.Models.ActivityUpgradePlanView",
        ["UpgradePlans.Get"] = "Elsa.Activities.Design.Api.Models.ActivityUpgradePlanView",
        ["UpgradePlans.Apply"] = "Elsa.Activities.Design.Api.Models.ActivityUpgradeApplyResultView",
        ["UpgradePlans.GetReceipt"] = "Elsa.Activities.Design.Api.Models.ActivityUpgradeApplyReceiptView",
        ["UpgradePlans.Refresh"] = "Elsa.Activities.Design.Api.Models.ActivityUpgradePlanView"
    };

    [Fact]
    public async Task Post_migration_real_host_exposes_exactly_the_reviewed_38_operations()
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();
        var endpoints = OwnedEndpoints(host).ToArray();

        Assert.Equal(38, endpoints.Length);
        Assert.Equal(38, endpoints.Select(endpoint => Identity(endpoint)).Distinct().Count());
        Assert.Equal(
            ActivitiesDesignCompatibilityCases.Manifest.Select(route => route.Endpoint).OrderBy(endpoint => endpoint.ToString()),
            endpoints.Select(Identity).OrderBy(endpoint => endpoint.ToString()));
    }

    [Fact]
    public async Task Every_operation_publishes_stable_name_tag_owner_authoring_security_accepts_and_produces_metadata()
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();

        foreach (var route in ActivitiesDesignCompatibilityCases.Manifest)
        {
            var endpoint = Assert.Single(OwnedEndpoints(host), candidate => Identity(candidate) == route.Endpoint);
            var name = endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName;
            Assert.Equal(OperationPrefix + Compact(route.Id), name);
            Assert.Equal(OwnerId, endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
            Assert.Equal([OwnerId], endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags);
            Assert.NotNull(endpoint.Metadata.GetMetadata<AuthorizeAttribute>());

            var disposition = Assert.IsType<EndpointSecurityDispositionMetadata>(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
            var policy = Assert.IsType<PermissionPolicyParseResult>(new PermissionPolicyCodec().Parse(disposition.Value!));
            Assert.Single(policy.Descriptor!.Permissions);
            Assert.DoesNotContain(PermissionKey.Normalize(PermissionKey.Wildcard), policy.Descriptor.Permissions);
            Assert.Equal(PermissionKey.Normalize(route.Action == "manage" ? "activity-design.manage" : "activity-design.read"), policy.Descriptor.Permissions.Single());

            var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
            var expectedRequest = RequestTypes[route.Id];
            if (expectedRequest is null)
                Assert.Null(accepts);
            else
            {
                Assert.NotNull(accepts);
                Assert.Equal(expectedRequest, accepts!.RequestType?.FullName);
                Assert.Contains("application/json", accepts.ContentTypes);
            }

            var produced = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
            Assert.Contains(produced, metadata => metadata.StatusCode == route.SuccessStatus);
            Assert.Contains(produced, metadata => metadata.StatusCode == StatusCodes.Status401Unauthorized);
            Assert.Contains(produced, metadata => metadata.StatusCode == StatusCodes.Status403Forbidden);
            var success = produced.Single(metadata => metadata.StatusCode == route.SuccessStatus);
            Assert.Equal(ResponseTypes[route.Id], TypeKey(success.Type));
        }
    }

    [Fact]
    public async Task Frozen_http_and_openapi_corpus_has_a_real_post_migration_replay_entry_point()
    {
        await using var host = await ActivitiesDesignMinimalApiHost.StartAsync();
        var afterHttp = (await Task.WhenAll(ActivitiesDesignCompatibilityCases.All.Select(testCase => CaptureAfterAsync(host.Client, testCase)))).ToArray();
        var afterOpenApi = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync(), includeIdentityMetadata: true);
        var directory = Path.Join(AppContext.BaseDirectory, "Baselines");
        var before = new CompatibilityEvidenceSet
        {
            Http = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(directory, "activities-design-http-fastendpoints.json")),
            OpenApi = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(directory, "activities-design-openapi-fastendpoints.json"))
        };
        var approvals = BaselineFile.Load<ApprovedDifference[]>(Path.Join(directory, "activities-design-approved-differences.json"));

        var comparison = CompatibilityComparer.CompareBidirectional(
            before,
            new CompatibilityEvidenceSet { Http = afterHttp, OpenApi = afterOpenApi },
            approvals);

        Assert.True(comparison.IsCompatible, string.Join(Environment.NewLine, comparison.Failures));
    }

    private static IEnumerable<RouteEndpoint> OwnedEndpoints(ActivitiesDesignMinimalApiHost host) =>
        host.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("design/activities", StringComparison.OrdinalIgnoreCase) == true);

    private static EndpointIdentity Identity(RouteEndpoint endpoint) => new(
        "/" + endpoint.RoutePattern.RawText!.TrimStart('/'),
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single());

    private static string Compact(string id) => string.Concat(id.Split('.').Select(segment => segment));

    private static string TypeKey(Type? type) => type?.IsGenericType == true
        ? type.GetGenericTypeDefinition().FullName!
        : type?.FullName ?? typeof(void).FullName!;

    private static async Task<HttpCompatibilityObservation> CaptureAfterAsync(HttpClient client, HttpCompatibilityCase testCase)
    {
        try
        {
            return NormalizeVolatileFields(await HttpEvidenceCapture.CaptureAsync(client, testCase));
        }
        catch (Exception exception)
        {
            var terminal = exception.GetBaseException();
            return new HttpCompatibilityObservation
            {
                Endpoint = testCase.Endpoint,
                Case = testCase.Case,
                Binding = testCase.Binding ?? "",
                PagingFiltering = testCase.PagingFiltering ?? "",
                StatusCode = 0,
                TerminalState = $"Faulted:{terminal.GetType().FullName}"
            };
        }
    }

    private static HttpCompatibilityObservation NormalizeVolatileFields(HttpCompatibilityObservation observation)
    {
        if (observation.Json.Length == 0 || JsonNode.Parse(observation.Json) is not JsonObject body || !body.ContainsKey("traceId"))
            return observation;

        body["traceId"] = "<volatile-trace-id>";
        var normalized = CompatibilityJson.Canonicalize(body.ToJsonString());
        return observation with
        {
            Json = normalized,
            Body = observation.Body == observation.Json ? normalized : observation.Body,
            ProblemDetails = observation.ProblemDetails == observation.Json ? normalized : observation.ProblemDetails
        };
    }
}
