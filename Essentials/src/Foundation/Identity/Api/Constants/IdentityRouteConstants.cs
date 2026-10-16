namespace Elsa.Foundation.Identity.Api.Constants;

internal static class IdentityRouteConstants
{
    internal const string Prefix = "_elsa/identity";

    internal const string ApplicationsRoute = "/" + Prefix + "/applications";

    internal const string ApplicationRoute = ApplicationsRoute + "/{applicationId}";

    internal const string ApplicationApiKeyRoute = ApplicationRoute + "/api-key";

    internal static string GetRoute(string path) => string.Join('/', Prefix, path.TrimStart('/'));
}
