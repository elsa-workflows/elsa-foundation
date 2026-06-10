namespace Elsa.Workflows.Runtime.Api.Constants;

internal static class RouteConstants
{
    internal const string DomainPrefix = "runtime/workflows";

    internal static string GetRoute(string path) => string.Join('/', DomainPrefix, path.TrimStart('/'));
}
