using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using System.Text;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

/// <summary>
/// The immutable-before corpus for the Publishing owner.  The route manifest is deliberately the
/// only source used to build the anonymous and authenticated sets so a newly discovered route cannot
/// silently bypass the historical contract capture.
/// </summary>
public static class PublishingCompatibilityCases
{
    public const string IdentityHeader = "X-Elsa-Publishing-Capture-Identity";

    public static IReadOnlyList<PublishingRoute> Manifest { get; } =
    [
        Route("Activities.List", "GET", "publishing/activities", "read", 200, "ListConstructableActivities"),
        Route("Activities.Construct", "GET", "publishing/activities/{activityId}/construct", "read", 200, "ConstructActivity"),
        Route("IncidentStrategies.List", "GET", "publishing/incident-strategies", "read", 200, "none"),
        Route("ValueConversionProfiles.List", "GET", "publishing/value-conversion/profiles", "read", 200, "none"),
        Route("WorkflowPreflight", "POST", "publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/preflight", "read", 200, "PreflightWorkflowPublication"),
        Route("WorkflowSnapshotPreflight", "POST", "publishing/workflows/preflight", "read", 200, "PreflightWorkflowPublicationSnapshot"),
        Route("PublicationSlots.List", "GET", "publishing/workflows/{definitionId}/slots", "read", 200, "ListPublicationSlots"),
        Route("PublicationSlots.Get", "GET", "publishing/workflows/{definitionId}/slots/{slotName}", "read", 200, "GetPublicationSlot"),
        Route("PublicationSlots.Unpublish", "DELETE", "publishing/workflows/{definitionId}/slots/{slotName}", "manage", 200, "UnpublishPublicationSlotRequest"),
        Route("PublicationSlots.Restore", "POST", "publishing/workflows/{definitionId}/slots/{slotName}/restore", "manage", 200, "RestorePublicationSlotRequest"),
        Route("WorkflowPolicy.Get", "GET", "publishing/workflows/{definitionId}/policy", "read", 200, "GetWorkflowPublicationPolicy"),
        Route("WorkflowPolicy.Set", "PUT", "publishing/workflows/{definitionId}/policy", "manage", 200, "SetWorkflowPublicationPolicy"),
        Route("WorkflowPublish", "POST", "publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/publish", "manage", 201, "PublishWorkflowRequest"),
        Route("WorkflowTestRuns.Start", "POST", "publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/test-runs", "manage", 200, "StartWorkflowTestRun"),
        Route("WorkflowTestRuns.StartDraft", "POST", "publishing/workflows/drafts/test-runs", "manage", 200, "StartWorkflowDraftTestRun"),
        Route("RuntimePreflight", "POST", "publishing/preflight", "read", 200, "RunRuntimeRequirementPreflight"),
        Route("ActivityDrafts.Preflight", "POST", "design/activities/drafts/{draftId}/publication-preflight", "read", 200, "PreflightActivityDraftPublication"),
        Route("ActivityDrafts.Publish", "POST", "design/activities/drafts/{draftId}/publish", "manage", 201, "PublishActivityDraft"),
        Route("ActivityPublications.GetReceipt", "GET", "design/activities/publications/{idempotencyKey}", "read", 200, "GetActivityPublicationReceipt"),
        Route("ActivityTestRuns.Start", "POST", "publishing/activity-drafts/{draftId}/test-runs", "manage", 202, "StartActivityDraftTestRun"),
        Route("ActivityTestRuns.Get", "GET", "publishing/activity-test-runs/{testRunId}", "manage", 200, "none"),
        Route("ActivityTestRuns.GetByIdempotencyKey", "GET", "publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}", "manage", 200, "none"),
        Route("ActivityTestRuns.Cancel", "POST", "publishing/activity-test-runs/{testRunId}/cancel", "manage", 202, "none")
    ];

    public static IReadOnlyList<HttpCompatibilityCase> Anonymous { get; } =
        Manifest.Select(route => Create(route, route.Id + "|anonymous")).ToArray();

    public static IReadOnlyList<HttpCompatibilityCase> Authenticated { get; } =
        Manifest.Select(route => Create(route, route.Id + "|trusted-success", "trusted-success", BodyFor(route))).ToArray();

    public static IReadOnlyList<HttpCompatibilityCase> Binding { get; } =
    [
        Create(Find("WorkflowSnapshotPreflight"), "WorkflowSnapshotPreflight|trusted-malformed-json", "trusted-binding", "{"),
        Create(Find("WorkflowPublish"), "WorkflowPublish|trusted-null-body", "trusted-binding", "null"),
        Create(Find("ActivityDrafts.Publish"), "ActivityDrafts.Publish|trusted-empty-body", "trusted-binding", string.Empty),
        Create(Find("ActivityDrafts.Preflight"), "ActivityDrafts.Preflight|trusted-wrong-content-type", "trusted-binding", "{}", "text/plain"),
        Create(Find("WorkflowPublish"), "WorkflowPublish|trusted-route-over-body", "trusted-binding", "{\"versionId\":\"body-version\",\"reviewToken\":\"review\"}"),
        Create(Find("WorkflowTestRuns.StartDraft"), "WorkflowTestRuns.StartDraft|trusted-reserved-drafts-selection", "trusted-success", BodyFor(Find("WorkflowTestRuns.StartDraft"))),
        Create(Find("ActivityTestRuns.Start"), "ActivityTestRuns.Start|trusted-route-over-body", "trusted-binding", "{\"draftId\":\"body-draft\",\"idempotencyKey\":\"capture\"}")
    ];

    public static IReadOnlyList<HttpCompatibilityCase> Domain { get; } =
    [
        Create(Find("PublicationSlots.Get"), "PublicationSlots.Get|trusted-domain-not-found", "trusted-domain-not-found", requestPath: "/publishing/workflows/missing-definition/slots/default"),
        Create(Find("WorkflowPolicy.Set"), "WorkflowPolicy.Set|trusted-domain-conflict", "trusted-domain-conflict", "{}"),
        Create(Find("WorkflowPublish"), "WorkflowPublish|trusted-domain-conflict", "trusted-domain-conflict", BodyFor(Find("WorkflowPublish"))),
        Create(Find("WorkflowPreflight"), "WorkflowPreflight|trusted-domain-validation", "trusted-domain-validation", "{\"action\":999}"),
        Create(Find("RuntimePreflight"), "RuntimePreflight|trusted-domain-unavailable", "trusted-domain-unavailable", BodyFor(Find("RuntimePreflight")))
    ];

    public static IReadOnlyList<HttpCompatibilityCase> Cancellation { get; } =
    [Create(Find("ActivityTestRuns.Get"), "ActivityTestRuns.Get|trusted-cancellation", "trusted-cancellation")];

    public static IReadOnlyList<HttpCompatibilityCase> All { get; } =
        Anonymous.Concat(Authenticated).Concat(Binding).Concat(Domain).Concat(Cancellation).ToArray();

    private static PublishingRoute Find(string id) => Manifest.Single(route => route.Id == id);

    private static PublishingRoute Route(string id, string method, string path, string action, int status, string request) =>
        new(id, new EndpointIdentity("/" + path, method), "/" + SamplePath(path), action, status, request);

    private static HttpCompatibilityCase Create(
        PublishingRoute route,
        string caseName,
        string? identity = null,
        string? body = null,
        string contentType = "application/json",
        string? requestPath = null) =>
        new(route.Endpoint, caseName, () =>
        {
            var request = new HttpRequestMessage(new HttpMethod(route.Endpoint.Method.Value), requestPath ?? route.RequestPath);
            if (identity is not null)
                request.Headers.TryAddWithoutValidation(IdentityHeader, identity);
            if (body is not null)
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
            return request;
        })
        {
            Binding = BindingDescription(route.RequestPath, body),
            PagingFiltering = QueryDescription(route.RequestPath)
        };

    private static string? BodyFor(PublishingRoute route) => route.Id switch
    {
        "WorkflowSnapshotPreflight" => "{\"definitionId\":\"definition-capture\",\"state\":{},\"layout\":[]}",
        "WorkflowPreflight" => "{\"versionId\":\"body-version\",\"slotName\":\"default\"}",
        "WorkflowPublish" => "{\"versionId\":\"body-version\",\"slotName\":\"default\",\"reviewToken\":\"capture-review\",\"idempotencyKey\":\"capture-publish\"}",
        "WorkflowTestRuns.Start" => "{\"versionId\":\"body-version\",\"input\":{}}",
        "WorkflowTestRuns.StartDraft" => "{\"definitionId\":\"definition-capture\",\"snapshotId\":\"snapshot-capture\",\"state\":{},\"inputs\":{}}",
        "RuntimePreflight" => "{\"scope\":\"ActiveRetainedArtifacts\",\"artifactIds\":null}",
        "ActivityDrafts.Preflight" => "{\"draftId\":\"body-draft\",\"expectedDraftRevision\":0}",
        "ActivityDrafts.Publish" => "{\"draftId\":\"body-draft\",\"expectedDraftRevision\":0,\"version\":\"1\",\"reviewToken\":\"capture-review\",\"idempotencyKey\":\"capture-publish\"}",
        "ActivityTestRuns.Start" => "{\"draftId\":\"body-draft\",\"input\":{},\"idempotencyKey\":\"capture-run\"}",
        "WorkflowPolicy.Set" => "{\"defaultAction\":\"Replace\",\"defaultSlotName\":\"default\",\"expectedRevision\":0}",
        "PublicationSlots.Unpublish" or "PublicationSlots.Restore" => "{}",
        _ when route.Endpoint.Method.Value == "GET" => null,
        _ => "{}"
    };

    private static string SamplePath(string path) => path
        .Replace("{activityId}", "activity-route", StringComparison.Ordinal)
        .Replace("{versionId:regex(^(?!drafts$).+$)}", "version-route", StringComparison.Ordinal)
        .Replace("{definitionId}", "definition-route", StringComparison.Ordinal)
        .Replace("{slotName}", "default", StringComparison.Ordinal)
        .Replace("{draftId}", "draft-route", StringComparison.Ordinal)
        .Replace("{idempotencyKey}", "idempotency-route", StringComparison.Ordinal)
        .Replace("{testRunId}", "test-run-route", StringComparison.Ordinal);

    private static string BindingDescription(string route, string? body)
    {
        var path = route.Split('?', 2)[0];
        var fields = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment.StartsWith('{') && segment.EndsWith('}'))
            .Select(segment => segment[1..^1]);
        var bodyKind = body is null ? "none" : body switch
        {
            "{" => "malformed",
            "null" => "null",
            _ when body.Length == 0 => "empty",
            _ => "json"
        };
        return $"route={string.Join(',', fields)};body={bodyKind}";
    }

    private static string QueryDescription(string route) =>
        route.Contains('?', StringComparison.Ordinal) ? $"query={route[(route.IndexOf('?'))..]};link=" : "";
}

public sealed record PublishingRoute(
    string Id,
    EndpointIdentity Endpoint,
    string RequestPath,
    string Action,
    int SuccessStatus,
    string Request)
{
    public string Response => Id switch
    {
        "Activities.List" => "IEnumerable<ConstructableActivityView>",
        "Activities.Construct" => "ConstructedActivityView",
        "IncidentStrategies.List" => "IncidentStrategiesResponse",
        "ValueConversionProfiles.List" => "ValueConversionProfilesResponse",
        "WorkflowPreflight" => "PublicationPreflightView",
        "WorkflowSnapshotPreflight" => "PublicationSnapshotPreflightView",
        "PublicationSlots.List" => "PublicationSlotListResponse",
        "PublicationSlots.Get" or "PublicationSlots.Unpublish" or "PublicationSlots.Restore" => "PublicationSlotView",
        "WorkflowPolicy.Get" or "WorkflowPolicy.Set" => "PublicationPolicyView",
        "WorkflowPublish" => "PublishedWorkflowView",
        "WorkflowTestRuns.Start" or "WorkflowTestRuns.StartDraft" => "WorkflowTestRunView",
        "RuntimePreflight" => "RuntimeRequirementPreflightView",
        "ActivityDrafts.Preflight" => "ActivityPublicationPreflightView",
        "ActivityDrafts.Publish" or "ActivityPublications.GetReceipt" => "ActivityPublicationReceiptView",
        "ActivityTestRuns.Start" or "ActivityTestRuns.Get" or "ActivityTestRuns.GetByIdempotencyKey" or "ActivityTestRuns.Cancel" => "ActivityDraftTestRunView",
        _ => throw new InvalidOperationException($"No response contract was recorded for '{Id}'.")
    };
}
