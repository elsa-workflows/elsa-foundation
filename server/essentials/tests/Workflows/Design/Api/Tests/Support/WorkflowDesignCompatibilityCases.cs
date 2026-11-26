using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using System.Text;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api.Tests.Support;

/// <summary>Stable, named requests covering every concrete Workflows Design registration.</summary>
public static class WorkflowDesignCompatibilityCases
{
    public const string IdentityHeader = "X-Workflow-Design-Identity";

    public static IReadOnlyList<HttpCompatibilityCase> All { get; } =
    [
        Post("analyze-scoped-variables", "/design/workflows/scoped-variables/analyze"),
        Post("complete-expression-tooling", "/design/workflows/expression-tooling/completions"),
        Get("describe-expression-tooling", "/design/workflows/expression-tooling/descriptors"),
        Post("hover-expression-tooling", "/design/workflows/expression-tooling/hover"),
        Post("resolve-activity-input-options", "/design/workflows/activities/sample/inputs/name/options"),
        Post("resolve-expression-tooling-context", "/design/workflows/expression-tooling/context"),
        Post("search-expression-tooling-symbols", "/design/workflows/expression-tooling/symbols"),
        Post("validate-expression-tooling", "/design/workflows/expression-tooling/validate"),
        Post("add-definition", "/design/workflows/definitions"),
        Delete("delete-definition", "/design/workflows/definitions/sample"),
        Delete("delete-definition-permanently", "/design/workflows/definitions/sample/permanent"),
        Get("get-definition", "/design/workflows/definitions/sample"),
        Get("list-definitions", "/design/workflows/definitions"),
        Post("restore-definition", "/design/workflows/definitions/sample/restore"),
        Post("submit-definition", "/design/workflows/definitions/submit"),
        Get("submit-definition-schema", "/design/workflows/definitions/submit/schema"),
        Patch("update-definition", "/design/workflows/definitions/sample"),
        Delete("discard-draft", "/design/workflows/drafts/sample"),
        Get("get-draft", "/design/workflows/drafts/sample"),
        Post("promote-draft", "/design/workflows/drafts/sample/promote"),
        Post("promotion-preflight", "/design/workflows/drafts/sample/promotion-preflight"),
        Put("replace-draft", "/design/workflows/drafts/sample"),
        Get("draft-validations", "/design/workflows/drafts/sample/validations"),
        Get("list-structures", "/design/workflows/structures"),
        Post("add-version", "/design/workflows/versions/ingest"),
        Get("get-version", "/design/workflows/versions/sample"),
        Get("list-versions", "/design/workflows/definitions/sample/versions"),
        Get("describe-expression-tooling|trusted-success", "/design/workflows/expression-tooling/descriptors", "trusted-success"),
        Post("analyze-scoped-variables|trusted-malformed-json", "/design/workflows/scoped-variables/analyze", "trusted-malformed-json", "{ malformed"),
        Post("add-definition|trusted-unsupported-content-type", "/design/workflows/definitions", "trusted-unsupported-content-type", "{}", "text/plain"),
        Get("list-definitions|paging-filtering", "/design/workflows/definitions?searchTerm=sample&tenantAgnostic=false&state=published", "trusted-paging"),
        Get("get-definition|trusted-not-found", "/design/workflows/definitions/sample", "trusted-not-found"),
        Post("promote-draft|trusted-404", "/design/workflows/drafts/sample/promote", "trusted-promote-404", BodyFor("promote-draft")),
        Post("promote-draft|trusted-409-concurrency", "/design/workflows/drafts/sample/promote", "trusted-promote-409", BodyFor("promote-draft")),
        Post("promote-draft|trusted-500", "/design/workflows/drafts/sample/promote", "trusted-promote-500", BodyFor("promote-draft")),
        Delete("delete-definition-permanently|trusted-501", "/design/workflows/definitions/sample/permanent", "trusted-delete-501", BodyFor("delete-definition-permanently")),
        Delete("delete-definition-permanently|trusted-404", "/design/workflows/definitions/sample/permanent", "trusted-delete-404", BodyFor("delete-definition-permanently")),
        Delete("delete-definition-permanently|trusted-409-not-soft-deleted", "/design/workflows/definitions/sample/permanent", "trusted-delete-409", BodyFor("delete-definition-permanently")),
        Delete("delete-definition-permanently|trusted-500", "/design/workflows/definitions/sample/permanent", "trusted-delete-500", BodyFor("delete-definition-permanently")),
        Post("promotion-preflight|trusted-nonmutation", "/design/workflows/drafts/sample/promotion-preflight", "trusted-preflight", "{\"requestedVersion\":\"1.0.0\"}"),
        Post("analyze-scoped-variables|trusted-success", "/design/workflows/scoped-variables/analyze", "trusted-success", "{\"state\":{\"activities\":[],\"connections\":[]},\"nodeId\":null}"),
        Post("complete-expression-tooling|trusted-success", "/design/workflows/expression-tooling/completions", "trusted-success", BodyFor("complete-expression-tooling")),
        Post("hover-expression-tooling|trusted-success", "/design/workflows/expression-tooling/hover", "trusted-success", BodyFor("hover-expression-tooling")),
        Post("resolve-activity-input-options|trusted-success", "/design/workflows/activities/sample/inputs/name/options", "trusted-success", BodyFor("resolve-activity-input-options")),
        Post("resolve-expression-tooling-context|trusted-success", "/design/workflows/expression-tooling/context", "trusted-success", BodyFor("resolve-expression-tooling-context")),
        Post("search-expression-tooling-symbols|trusted-success", "/design/workflows/expression-tooling/symbols", "trusted-success", BodyFor("resolve-expression-tooling-context")),
        Post("validate-expression-tooling|trusted-success", "/design/workflows/expression-tooling/validate", "trusted-success", BodyFor("validate-expression-tooling")),
        Post("add-definition|trusted-success", "/design/workflows/definitions", "trusted-success", BodyFor("add-definition")),
        Delete("delete-definition|trusted-success", "/design/workflows/definitions/sample", "trusted-success", BodyFor("delete-definition")),
        Delete("delete-definition-permanently|trusted-success", "/design/workflows/definitions/sample/permanent", "trusted-success", BodyFor("delete-definition-permanently")),
        Get("get-definition|trusted-success", "/design/workflows/definitions/sample", "trusted-success"),
        Get("list-definitions|trusted-success", "/design/workflows/definitions", "trusted-success"),
        Post("restore-definition|trusted-success", "/design/workflows/definitions/sample/restore", "trusted-success", BodyFor("restore-definition")),
        Post("submit-definition|trusted-success", "/design/workflows/definitions/submit", "trusted-success", BodyFor("submit-definition")),
        Get("submit-definition-schema|trusted-success", "/design/workflows/definitions/submit/schema", "trusted-success"),
        Patch("update-definition|trusted-success", "/design/workflows/definitions/sample", "trusted-success", BodyFor("update-definition")),
        Delete("discard-draft|trusted-success", "/design/workflows/drafts/sample", "trusted-success", BodyFor("discard-draft")),
        Get("get-draft|trusted-success", "/design/workflows/drafts/sample", "trusted-success"),
        Post("promote-draft|trusted-success", "/design/workflows/drafts/sample/promote", "trusted-success", BodyFor("promote-draft")),
        Post("promotion-preflight|trusted-success", "/design/workflows/drafts/sample/promotion-preflight", "trusted-success", BodyFor("promotion-preflight")),
        Put("replace-draft|trusted-success", "/design/workflows/drafts/sample", "trusted-success", BodyFor("replace-draft")),
        Get("draft-validations|trusted-success", "/design/workflows/drafts/sample/validations", "trusted-success"),
        Get("list-structures|trusted-success", "/design/workflows/structures", "trusted-success"),
        Post("add-version|trusted-success", "/design/workflows/versions/ingest", "trusted-success", BodyFor("add-version")),
        Get("get-version|trusted-success", "/design/workflows/versions/sample", "trusted-success"),
        Get("list-versions|trusted-success", "/design/workflows/definitions/sample/versions", "trusted-success"),
        Delete("delete-definition|trusted-missing-body", "/design/workflows/definitions/sample", "trusted-manage"),
        Delete("delete-definition|trusted-malformed-json", "/design/workflows/definitions/sample", "trusted-manage", "{"),
        Delete("delete-definition|trusted-unsupported-content-type", "/design/workflows/definitions/sample", "trusted-manage", "{}", "text/plain"),
        Delete("delete-definition-permanently|trusted-missing-body", "/design/workflows/definitions/sample/permanent", "trusted-manage"),
        Delete("delete-definition-permanently|trusted-malformed-json", "/design/workflows/definitions/sample/permanent", "trusted-manage", "{"),
        Delete("delete-definition-permanently|trusted-unsupported-content-type", "/design/workflows/definitions/sample/permanent", "trusted-manage", "{}", "text/plain"),
        Post("restore-definition|trusted-missing-body", "/design/workflows/definitions/sample/restore", "trusted-manage"),
        Post("restore-definition|trusted-malformed-json", "/design/workflows/definitions/sample/restore", "trusted-manage", "{"),
        Post("restore-definition|trusted-unsupported-content-type", "/design/workflows/definitions/sample/restore", "trusted-manage", "{}", "text/plain"),
        Delete("discard-draft|trusted-missing-body", "/design/workflows/drafts/sample", "trusted-manage"),
        Delete("discard-draft|trusted-malformed-json", "/design/workflows/drafts/sample", "trusted-manage", "{"),
        Delete("discard-draft|trusted-unsupported-content-type", "/design/workflows/drafts/sample", "trusted-manage", "{}", "text/plain")
    ];

    public static IReadOnlyList<HttpCompatibilityCase> Anonymous => All.Take(27).ToArray();

    private static HttpCompatibilityCase Get(string name, string route) => Create(HttpMethod.Get, name, route);
    private static HttpCompatibilityCase Get(string name, string route, string identity) => Create(HttpMethod.Get, name, route, identity);
    private static HttpCompatibilityCase Delete(string name, string route, string? identity = null, string? body = null, string contentType = "application/json") => Create(HttpMethod.Delete, name, route, identity, body, contentType);
    private static HttpCompatibilityCase Post(string name, string route, string? identity = null, string? body = null, string contentType = "application/json") => Create(HttpMethod.Post, name, route, identity, body, contentType);
    private static HttpCompatibilityCase Put(string name, string route, string? identity = null, string? body = null) => Create(HttpMethod.Put, name, route, identity, body);
    private static HttpCompatibilityCase Patch(string name, string route, string? identity = null, string? body = null) => Create(HttpMethod.Patch, name, route, identity, body);

    private static HttpCompatibilityCase Create(HttpMethod method, string name, string route, string? identity = null, string? body = null, string contentType = "application/json") =>
        new(new EndpointIdentity(EndpointRoute(route), method.Method), name, () =>
        {
            var request = new HttpRequestMessage(method, route);
            if (identity is not null)
                request.Headers.TryAddWithoutValidation(IdentityHeader, identity);
            if (method != HttpMethod.Get && body is not null)
                request.Content = new StringContent(body ?? "{}", Encoding.UTF8, contentType);
            return request;
        })
        {
            Binding = $"route={RouteFields(route)};query={Query(route)};body={BodyFields(body)}",
            PagingFiltering = Query(route)
        };

    private static string BodyFields(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "none";
        try
        {
            using var document = JsonDocument.Parse(body);
            return string.Join(',', document.RootElement.EnumerateObject().Select(property => property.Name));
        }
        catch (JsonException)
        {
            return "malformed";
        }
    }
    private static string RouteFields(string route) => string.Join(',', new[] { route.Contains("activities/", StringComparison.Ordinal) ? "activityVersionId" : null, route.Contains("inputs/name", StringComparison.Ordinal) ? "inputName" : null, route.Contains("definitions/sample", StringComparison.Ordinal) ? "definitionId" : null, route.Contains("drafts/sample", StringComparison.Ordinal) ? "draftId" : null, route.Contains("versions/sample", StringComparison.Ordinal) ? "versionId" : null }.Where(value => value is not null));
    private static string Query(string route) => route.Contains('?', StringComparison.Ordinal) ? $"query=?{route.Split('?', 2)[1]};link=" : "";

    private static string BodyFor(string name) => name switch
    {
        "complete-expression-tooling" => "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol500\",\"cursor\":{\"line\":0,\"character\":14}}",
        "hover-expression-tooling" => "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol500\",\"position\":{\"line\":0,\"character\":14}}",
        "resolve-expression-tooling-context" => "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"contextRevision\":null,\"search\":\"symbol\",\"skip\":0,\"take\":20}",
        "validate-expression-tooling" => "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"JavaScript\",\"documentRevision\":\"document\",\"source\":\"args.symbol500\"}",
        "resolve-activity-input-options" => "{\"activityVersionId\":\"activity\",\"inputName\":\"name\",\"nodeId\":null,\"workflowState\":null}",
        "add-definition" => "{\"operationKey\":\"capture-add\",\"name\":\"Capture definition\",\"description\":\"capture\"}",
        "submit-definition" => "{\"operationKey\":\"capture-submit\",\"name\":\"Capture definition\",\"description\":\"capture\",\"state\":{\"activities\":[],\"connections\":[]}}",
        "update-definition" => "{\"operationKey\":\"capture-update\",\"name\":\"Capture definition\",\"description\":\"capture\"}",
        "delete-definition" => "{\"operationKey\":\"capture-delete\",\"definitionId\":\"body-definition\",\"reason\":\"capture\"}",
        "delete-definition-permanently" => "{\"operationKey\":\"capture-permanent\",\"definitionId\":\"body-definition\"}",
        "restore-definition" => "{\"operationKey\":\"capture-restore\",\"definitionId\":\"body-definition\"}",
        "discard-draft" => "{\"operationKey\":\"capture-discard\",\"draftId\":\"body-draft\"}",
        "promote-draft" => "{\"operationKey\":\"capture-promote\",\"draftId\":\"body-draft\",\"requestedVersion\":\"1.0.0\"}",
        "promotion-preflight" => "{\"draftId\":\"body-draft\",\"requestedVersion\":\"1.0.0\"}",
        "replace-draft" => "{\"operationKey\":\"capture-replace\",\"draftId\":\"body-draft\",\"state\":{\"activities\":[],\"connections\":[]}}",
        "add-version" => "{\"operationKey\":\"capture-version\",\"definitionId\":\"body-definition\",\"state\":{\"activities\":[],\"connections\":[]}}",
        _ => "{}"
    };

    private static string EndpointRoute(string route) => route.Split('?', 2)[0]
        .Replace("sample", "{param}", StringComparison.Ordinal)
        .Replace("name", "{param}", StringComparison.Ordinal);
}
