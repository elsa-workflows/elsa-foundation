namespace Elsa.Tagging.Api.Constants;

internal static class RouteConstants
{
    internal const string Definitions = "tagging/definitions";
    internal static string Definition(string tagDefinitionId) => Definitions + "/" + tagDefinitionId;
    internal static string Values(string tagDefinitionId) => Definition(tagDefinitionId) + "/values";
    internal static string Value(string tagDefinitionId, string controlledTagValueId) => Values(tagDefinitionId) + "/" + controlledTagValueId;
}
