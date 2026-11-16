using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using System.Text;

namespace Elsa.Activities.Design.Tests.Api.Support;

/// <summary>
/// The immutable before corpus.  The manifest is deliberately kept beside the cases so a route can
/// never be added to the capture host without also getting an anonymous and authenticated observation.
/// </summary>
public static class ActivitiesDesignCompatibilityCases
{
    public const string IdentityHeader = "X-Activities-Design-Capture-Identity";

    public static IReadOnlyList<ActivityDesignRoute> Manifest { get; } =
    [
        Route("Availability.GetSettings", "GET", "/availability/settings", "read", 200),
        Route("Availability.ListDiagnostics", "GET", "/availability/diagnostics", "read", 200, "?scope=host-default"),
        Route("Availability.SaveSettings", "PUT", "/availability/settings", "manage", 200),
        Route("AuthoringCapabilities.Get", "GET", "/authoring-capabilities", "read", 200),
        Route("Catalog.List", "GET", "/catalog", "read", 200, "?availability=All"),
        Route("Definitions.Add", "POST", "/definitions", "manage", 201),
        Route("Definitions.PreviewFork", "POST", "/definitions/{definitionId}/fork-previews", "manage", 200),
        Route("Definitions.List", "GET", "/definitions", "read", 200, "?limit=25&cursor=cursor-definitions&search=capture&authority=all&providerKey=capture-provider&sort=identity-desc"),
        Route("Definitions.Get", "GET", "/definitions/{definitionId}", "read", 200),
        Route("Definitions.Update", "PATCH", "/definitions/{definitionId}", "manage", 200),
        Route("Definitions.Recommendation", "PUT", "/definitions/{definitionId}/recommendation", "manage", 200),
        Route("Definitions.Picker", "GET", "/definitions/picker", "read", 200),
        Route("Definitions.ListDrafts", "GET", "/definitions/{definitionId}/drafts", "read", 200, "?limit=10&cursor=cursor-drafts&search=capture&providerKey=capture-provider&status=Draft&sort=updated-desc"),
        Route("Definitions.AddDraft", "POST", "/definitions/{definitionId}/drafts", "manage", 201),
        Route("Definitions.ListVersions", "GET", "/definitions/{definitionId}/versions", "read", 200, "?limit=10&cursor=cursor-versions&search=capture&providerKey=capture-provider&lifecycle=Active&sort=version-desc"),
        Route("Drafts.Get", "GET", "/drafts/{draftId}", "read", 200),
        Route("Drafts.Replace", "PUT", "/drafts/{draftId}", "manage", 200),
        Route("Drafts.UpdatePresentation", "PATCH", "/drafts/{draftId}/presentation", "manage", 200),
        Route("Drafts.ConflictCopy", "POST", "/drafts/{draftId}/conflict-copies", "manage", 201),
        Route("Drafts.Validate", "POST", "/drafts/{draftId}/validate", "manage", 200),
        Route("Drafts.MigrateProvider", "POST", "/drafts/{draftId}/migrate-provider", "manage", 201),
        Route("Drafts.ProposeContract", "POST", "/drafts/{draftId}/contract-proposals", "manage", 200),
        Route("Drafts.ApplyContractProposal", "POST", "/drafts/{draftId}/contract-proposals/apply", "manage", 200),
        Route("Drafts.Discard", "DELETE", "/drafts/{draftId}", "manage", 204),
        Route("Drafts.Diff", "POST", "/drafts/{draftId}/diff", "read", 200),
        Route("Forks.Apply", "POST", "/fork-candidates/{candidateId}/apply", "manage", 201),
        Route("Forks.GetStatus", "GET", "/forks/{idempotencyKey}", "read", 200),
        Route("Versions.Dependencies", "GET", "/versions/{versionId}/dependencies", "read", 200, "?direction=inbound&transitive=true&include=definitions&cursor=cursor-dependencies&limit=10"),
        Route("Versions.Diff", "GET", "/versions/{fromVersionId}/diff/{toVersionId}", "read", 200),
        Route("Versions.Get", "GET", "/versions/{versionId}", "read", 200),
        Route("Versions.Retire", "POST", "/versions/{versionId}/retire", "manage", 200),
        Route("Versions.Restore", "POST", "/versions/{versionId}/restore", "manage", 200),
        Route("Versions.Revoke", "POST", "/versions/{versionId}/revoke", "manage", 200),
        Route("UpgradePlans.Create", "POST", "/upgrade-plans", "manage", 201),
        Route("UpgradePlans.Get", "GET", "/upgrade-plans/{planId}", "read", 200),
        Route("UpgradePlans.Apply", "POST", "/upgrade-plans/{planId}/apply", "manage", 200),
        Route("UpgradePlans.GetReceipt", "GET", "/upgrade-plans/{planId}/receipts/{receiptId}", "read", 200),
        Route("UpgradePlans.Refresh", "POST", "/upgrade-plans/{planId}/refresh", "manage", 201)
    ];

    public static IReadOnlyList<HttpCompatibilityCase> Anonymous { get; } =
        Manifest.Select(route => Create(route, route.Id + "|anonymous")).ToArray();

    public static IReadOnlyList<HttpCompatibilityCase> Authenticated { get; } =
        Manifest.Where(route => route.Id != "Forks.GetStatus")
            .Select(route => Create(route, route.Id + "|trusted-success", "trusted-success", BodyFor(route)))
            .ToArray();

    /// <summary>
    /// Captures a real historical defect: FastEndpoints rejects the route-only, JSON-ignored request DTO
    /// before <c>Forks.GetStatus</c> reaches its handler. The Minimal API correction requires an explicit approval.
    /// </summary>
    public static IReadOnlyList<HttpCompatibilityCase> HistoricalDefects { get; } =
    [Create(Find("Forks.GetStatus"), "Forks.GetStatus|trusted-route-only-binding-failure", "trusted-success")];

    public static IReadOnlyList<HttpCompatibilityCase> Binding { get; } =
    [
        Create(Find("Definitions.Add"), "Definitions.Add|trusted-malformed-json", "trusted-binding", "{"),
        Create(Find("Definitions.Add"), "Definitions.Add|trusted-unsupported-content-type", "trusted-binding", "{}", "text/plain"),
        Create(Find("Drafts.Replace"), "Drafts.Replace|trusted-empty-body", "trusted-binding", string.Empty),
        Create(Find("UpgradePlans.Apply"), "UpgradePlans.Apply|trusted-route-over-body", "trusted-binding", "{\"planId\":\"body-plan\",\"stageId\":\"stage\",\"idempotencyKey\":\"key\"}")
    ];

    public static IReadOnlyList<HttpCompatibilityCase> Domain { get; } =
    [
        Create(Find("Availability.GetSettings"), "Availability.GetSettings|trusted-domain-not-found", "trusted-domain-not-found"),
        Create(Find("Drafts.Validate"), "Drafts.Validate|trusted-domain-conflict", "trusted-domain-conflict", "{}"),
        Create(Find("UpgradePlans.Apply"), "UpgradePlans.Apply|trusted-domain-failure", "trusted-domain-failure", "{}")
    ];

    public static IReadOnlyList<HttpCompatibilityCase> Cancellation { get; } =
    [Create(Find("Drafts.Get"), "Drafts.Get|trusted-cancellation", "trusted-cancellation")];

    public static IReadOnlyList<HttpCompatibilityCase> All { get; } =
        Anonymous.Concat(Authenticated).Concat(HistoricalDefects).Concat(Binding).Concat(Domain).Concat(Cancellation).ToArray();

    private static ActivityDesignRoute Find(string id) => Manifest.Single(route => route.Id == id);

    public static string? RequestBodyFor(string id) => BodyFor(Find(id));

    private static ActivityDesignRoute Route(string id, string method, string path, string action, int success, string? query = null)
    {
        var endpoint = new EndpointIdentity("/design/activities" + path, method);
        return new(id, endpoint, "/design/activities" + SamplePath(path) + query, action, success);
    }

    private static HttpCompatibilityCase Create(
        ActivityDesignRoute route,
        string caseName,
        string? identity = null,
        string? body = null,
        string contentType = "application/json") =>
        new(route.Endpoint, caseName, () =>
        {
            var request = new HttpRequestMessage(new HttpMethod(route.Endpoint.Method.Value), route.RequestPath);
            if (identity is not null)
                request.Headers.TryAddWithoutValidation(IdentityHeader, identity);
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
            }

            return request;
        })
        {
            Binding = BindingDescription(route.RequestPath, body),
            PagingFiltering = QueryDescription(route.RequestPath)
        };

    private static string? BodyFor(ActivityDesignRoute route) => route.Id switch
    {
        "Availability.SaveSettings" => "{\"scope\":\"host-default\",\"mode\":\"AllExcept\",\"rules\":{\"activityTypes\":[],\"sets\":[]}}",
        "Definitions.Add" => "{\"category\":\"Capture\",\"displayName\":\"Capture activity\",\"description\":\"capture\",\"provider\":{\"providerKey\":\"capture-provider\",\"schemaVersion\":\"1\",\"payload\":{\"opaque\":true}},\"contract\":{\"contractSchemaVersion\":\"1\",\"inputs\":[],\"outputs\":[],\"outcomes\":[]},\"layout\":[]}",
        "Definitions.PreviewFork" => "{\"definitionId\":\"body-definition\",\"idempotencyKey\":\"capture-preview\",\"sourceVersionId\":\"source-version\",\"category\":\"Capture\",\"displayName\":\"Capture\",\"targetProviderKey\":\"capture-provider\",\"targetProviderSchemaVersion\":\"1\"}",
        "Definitions.Update" => "{\"definitionId\":\"body-definition\",\"category\":\"Capture\",\"displayName\":\"Capture activity\",\"description\":\"updated\"}",
        "Definitions.Recommendation" => "{\"expectedDefinitionHeadVersionId\":\"head\",\"expectedRecommendedVersionId\":\"current\",\"recommendedVersionId\":\"target\",\"expectedRecommendedVersionLifecycle\":\"Active\",\"reason\":\"capture\"}",
        "Definitions.AddDraft" => "{\"definitionId\":\"body-definition\",\"sourceVersionId\":\"source-version\",\"presentationLabel\":\"Capture draft\"}",
        "Drafts.Replace" => "{\"draftId\":\"body-draft\",\"expectedRevision\":1,\"contract\":{\"contractSchemaVersion\":\"1\",\"inputs\":[],\"outputs\":[],\"outcomes\":[]},\"provider\":{\"providerKey\":\"capture-provider\",\"schemaVersion\":\"1\",\"payload\":{\"opaque\":true}},\"layout\":[],\"presentationLabel\":\"Capture draft\"}",
        "Drafts.UpdatePresentation" => "{\"draftId\":\"body-draft\",\"expectedRevision\":1,\"presentationLabel\":\"Capture draft\"}",
        "Drafts.ConflictCopy" => "{\"draftId\":\"body-draft\",\"expectedSourceRevision\":1,\"contract\":{\"contractSchemaVersion\":\"1\",\"inputs\":[],\"outputs\":[],\"outcomes\":[]},\"provider\":{\"providerKey\":\"capture-provider\",\"schemaVersion\":\"1\",\"payload\":{\"opaque\":true}},\"layout\":[],\"presentationLabel\":\"Recovered capture\"}",
        "Drafts.Validate" => "{\"draftId\":\"body-draft\",\"expectedRevision\":1}",
        "Drafts.MigrateProvider" => "{\"draftId\":\"body-draft\",\"expectedRevision\":1,\"targetProviderKey\":\"capture-provider\",\"targetSchemaVersion\":\"2\"}",
        "Drafts.ProposeContract" => "{\"draftId\":\"body-draft\",\"expectedRevision\":1,\"expectedProviderKey\":\"capture-provider\",\"expectedProviderSchemaVersion\":\"1\",\"expectedManifestFingerprint\":\"sha256:manifest\"}",
        "Drafts.ApplyContractProposal" => "{\"draftId\":\"body-draft\",\"expectedRevision\":1,\"expectedProviderKey\":\"capture-provider\",\"expectedProviderSchemaVersion\":\"1\",\"expectedManifestFingerprint\":\"sha256:manifest\",\"proposalFingerprint\":\"sha256:proposal\",\"selectedChangeIds\":[]}",
        "Drafts.Discard" => "{\"draftId\":\"body-draft\",\"expectedRevision\":1}",
        "Drafts.Diff" => "{\"draftId\":\"body-draft\",\"expectedRevision\":1,\"baseVersionId\":\"base-version\"}",
        "Forks.Apply" => "{\"candidateId\":\"body-candidate\",\"requestFingerprint\":\"sha256:request\",\"idempotencyKey\":\"capture-fork\"}",
        "Versions.Retire" => "{\"versionId\":\"body-version\",\"expectedLifecycle\":\"Active\",\"reason\":\"capture\"}",
        "Versions.Restore" => "{\"versionId\":\"body-version\",\"expectedLifecycle\":\"Retired\",\"reason\":\"capture\"}",
        "Versions.Revoke" => "{\"versionId\":\"body-version\",\"expectedLifecycle\":\"Active\",\"reason\":\"capture\"}",
        "UpgradePlans.Create" => "{\"replacements\":[],\"roots\":[],\"includeTransitiveDependents\":true,\"createDraftsForPublishedDependents\":false}",
        "UpgradePlans.Apply" => "{\"planId\":\"body-plan\",\"stageId\":\"capture-stage\",\"idempotencyKey\":\"capture-apply\"}",
        "UpgradePlans.Refresh" => "{\"planId\":\"body-plan\",\"publications\":[]}",
        _ when route.Endpoint.Method.Value.Equals("GET", StringComparison.OrdinalIgnoreCase) => null,
        _ => "{}"
    };

    private static string SamplePath(string path) => path
        .Replace("{definitionId}", "definition-route", StringComparison.Ordinal)
        .Replace("{draftId}", "draft-route", StringComparison.Ordinal)
        .Replace("{candidateId}", "candidate-route", StringComparison.Ordinal)
        .Replace("{idempotencyKey}", "idempotency-route", StringComparison.Ordinal)
        .Replace("{versionId}", "version-route", StringComparison.Ordinal)
        .Replace("{fromVersionId}", "from-version-route", StringComparison.Ordinal)
        .Replace("{toVersionId}", "to-version-route", StringComparison.Ordinal)
        .Replace("{planId}", "plan-route", StringComparison.Ordinal)
        .Replace("{receiptId}", "receipt-route", StringComparison.Ordinal);

    private static string BindingDescription(string route, string? body)
    {
        var path = route.Split('?', 2)[0];
        var routeFields = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment.StartsWith('{') && segment.EndsWith('}'))
            .Select(segment => segment[1..^1]);
        var bodyFields = string.IsNullOrWhiteSpace(body) ? "none" :
            body == "{" ? "malformed" :
            body.Length == 0 ? "empty" : "json";
        return $"route={string.Join(',', routeFields)};query={QueryDescription(route)};body={bodyFields}";
    }

    private static string QueryDescription(string route)
    {
        var query = route.Split('?', 2).ElementAtOrDefault(1);
        return string.IsNullOrWhiteSpace(query) ? string.Empty : $"query=?{query};link=";
    }
}

public sealed record ActivityDesignRoute(
    string Id,
    EndpointIdentity Endpoint,
    string RequestPath,
    string Action,
    int SuccessStatus);
