namespace Elsa.Tagging.Api.Constants;

internal static class RouteConstants
{
    internal const string Definitions = "tagging/definitions";
    internal static string Definition(string tagDefinitionId) => Definitions + "/" + tagDefinitionId;
}
