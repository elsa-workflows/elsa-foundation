using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using System.Text;

namespace Elsa.Workflows.Design.Api.Tests.Support;

/// <summary>Stable, named requests covering every concrete Workflows Design FastEndpoints registration.</summary>
public static class WorkflowDesignCompatibilityCases
{
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
        Get("list-versions", "/design/workflows/definitions/sample/versions")
    ];

    private static HttpCompatibilityCase Get(string name, string route) => Create(HttpMethod.Get, name, route);
    private static HttpCompatibilityCase Delete(string name, string route) => Create(HttpMethod.Delete, name, route);
    private static HttpCompatibilityCase Post(string name, string route) => Create(HttpMethod.Post, name, route);
    private static HttpCompatibilityCase Put(string name, string route) => Create(HttpMethod.Put, name, route);
    private static HttpCompatibilityCase Patch(string name, string route) => Create(HttpMethod.Patch, name, route);

    private static HttpCompatibilityCase Create(HttpMethod method, string name, string route) =>
        new(new EndpointIdentity(EndpointRoute(route), method.Method), name, () =>
        {
            var request = new HttpRequestMessage(method, route);
            if (method != HttpMethod.Get && method != HttpMethod.Delete)
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return request;
        })
        {
            Binding = "route=definitionId,draftId,versionId,activityVersionId,inputName;body=request",
            PagingFiltering = ""
        };

    private static string EndpointRoute(string route) => route
        .Replace("sample", "{param}", StringComparison.Ordinal)
        .Replace("name", "{param}", StringComparison.Ordinal);
}
