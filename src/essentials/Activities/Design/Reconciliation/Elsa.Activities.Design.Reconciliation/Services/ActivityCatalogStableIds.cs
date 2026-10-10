using System.Security.Cryptography;
using System.Text;
using Elsa.Primitives.Versioning;

namespace Elsa.Activities.Design.Reconciliation.Services;

internal static class ActivityCatalogStableIds
{
    private const string DefinitionIdPrefix = "actdef";
    private const string VersionIdPrefix = "actver";

    public static string? DefinitionId(string activityTypeKey)
    {
        if (string.IsNullOrWhiteSpace(activityTypeKey))
            return null;

        return StableId(DefinitionIdPrefix, activityTypeKey);
    }

    public static string? VersionId(string activityTypeKey, string version)
    {
        if (string.IsNullOrWhiteSpace(activityTypeKey))
            return null;

        var logicalVersion = SemVer.ToSortKey(version);
        return StableId(VersionIdPrefix, activityTypeKey, logicalVersion);
    }

    private static string StableId(string prefix, params string[] parts)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\u001f', parts));
        var hash = SHA256.HashData(bytes);
        var encoded = Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"{prefix}_{encoded}";
    }
}
