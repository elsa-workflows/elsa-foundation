namespace Elsa.Secrets.Api.Constants;

internal static class RouteConstants
{
    internal const string DomainPrefix = "secrets";

    internal static string GetRoute(string path) => string.Join('/', DomainPrefix, path.TrimStart('/'));
}
