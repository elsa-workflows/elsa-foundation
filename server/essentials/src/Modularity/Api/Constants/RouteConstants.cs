namespace Elsa.Modularity.Api.Constants;

internal static class RouteConstants
{
    internal const string DomainPrefix = "modularity/features";

    internal static string GetRoute(string path) => string.Join('/', DomainPrefix, path.TrimStart('/'));
}
