using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr.Services;

namespace Elsa.Migration.Host.Tests.Support;

/// <summary>
/// The catalog version id CLR reconciliation gives an activity type, computed the way the catalog computes it: the type's
/// full name and the version its <c>[Version]</c> attribute or assembly gives, reduced to its SemVer sort key.
/// </summary>
internal static class CatalogIds
{
    public static string VersionIdOf(Type activityType)
    {
        var version = new ActivityTypeVersionResolver().Resolve(activityType, activityType.Assembly);
        return ActivityCatalogStableIds.VersionId(activityType.FullName!, version)
               ?? throw new InvalidOperationException($"No catalog version id for '{activityType.FullName}' {version}.");
    }

    public static string VersionIdOf(string activityTypeKey, string version) =>
        ActivityCatalogStableIds.VersionId(activityTypeKey, version)
        ?? throw new InvalidOperationException($"No catalog version id for '{activityTypeKey}' {version}.");
}
