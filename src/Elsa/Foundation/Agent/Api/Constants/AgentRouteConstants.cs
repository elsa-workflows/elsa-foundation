namespace Elsa.Foundation.Agent.Api.Constants;

internal static class AgentRouteConstants
{
    internal const string Prefix = "_elsa/agent";

    internal static string GetRoute(string path) => string.Join('/', Prefix, path.TrimStart('/'));
}
