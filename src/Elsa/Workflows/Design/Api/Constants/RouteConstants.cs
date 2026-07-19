namespace Elsa.Workflows.Design.Api.Constants;

internal static class RouteConstants
{
    internal const string DomainPrefix = "design/workflows";

    internal static string GetRoute(string path) => string.Join('/', DomainPrefix, path.TrimStart('/'));

    internal static string Definitions => GetRoute("definitions");

    internal static string Folders => GetRoute("folders");

    internal static string Versions => GetRoute("versions");

    internal static string ScopedVariableAnalysis => GetRoute("scoped-variables/analyze");

    internal static string ActivityInputOptions => GetRoute("activities/{activityVersionId}/inputs/{inputName}/options");
}
