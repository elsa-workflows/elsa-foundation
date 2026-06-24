namespace Elsa.Workflows.Publishing.Api.Constants;

internal static class RouteConstants
{
    internal const string DomainPrefix = "publishing";

    internal static string GetRoute(string path) => string.Join('/', DomainPrefix, path.TrimStart('/'));

    internal static string Activities => GetRoute("activities");
    internal static string WorkflowTestRuns(string versionId) => GetRoute($"workflows/{versionId}/test-runs");
    internal static string WorkflowDraftTestRuns => GetRoute("workflows/drafts/test-runs");
}
