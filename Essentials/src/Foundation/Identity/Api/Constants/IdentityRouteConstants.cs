namespace Elsa.Foundation.Identity.Api.Constants;

internal static class IdentityRouteConstants
{
    internal const string Prefix = "_elsa/identity";

    internal static string GetRoute(string path) => string.Join('/', Prefix, path.TrimStart('/'));
}
