namespace Elsa.Activities.Design.Api.Constants;

internal static class RouteConstants
{
    internal const string DomainPrefix = "design/activities";

    internal static string GetRoute(string path) => string.Join('/', DomainPrefix, path.TrimStart('/'));

    internal static string Definitions => GetRoute("definitions");

    internal static string Versions => GetRoute("versions");

    internal static string Catalog => GetRoute("catalog");
}
