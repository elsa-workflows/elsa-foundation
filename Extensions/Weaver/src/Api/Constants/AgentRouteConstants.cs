namespace Elsa.Weaver.Api.Constants;

internal static class AgentRouteConstants
{
    internal const string Prefix = "_elsa/weaver";

    internal static string GetRoute(string path) => string.Join('/', Prefix, path.TrimStart('/'));
}
