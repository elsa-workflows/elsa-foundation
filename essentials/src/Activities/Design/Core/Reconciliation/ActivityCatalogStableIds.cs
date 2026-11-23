using Elsa.Primitives.Versioning;

namespace Elsa.Activities.Design.Core.Reconciliation;

/// <summary>
/// Computes the stable ids the activity reconciler assigns to the definitions and versions an
/// <see cref="IActivityReconciliationSource"/> contributes. A source can call this to know an entry's
/// version id ahead of reconciliation, for example to reference that activity from a workflow it also contributes.
/// </summary>
public static class ActivityCatalogStableIds
{
    /// <summary>
    /// Returns the definition id for an activity type key, or <c>null</c> when the key is blank.
    /// Used when an entry does not carry its own <c>Id</c>.
    /// </summary>
    public static string? DefinitionId(string activityTypeKey)
    {
        if (string.IsNullOrWhiteSpace(activityTypeKey))
            return null;

        return ActivityStableIdFormula.DefinitionId(activityTypeKey);
    }

    /// <summary>
    /// Returns the version id for an activity type key and SemVer version, or <c>null</c> when the key is blank.
    /// Build metadata does not affect the id, because the version is reduced to its SemVer sort key first.
    /// </summary>
    /// <exception cref="ArgumentException">The version is not valid SemVer.</exception>
    public static string? VersionId(string activityTypeKey, string version)
    {
        if (string.IsNullOrWhiteSpace(activityTypeKey))
            return null;

        return ActivityStableIdFormula.VersionId(activityTypeKey, SemVer.ToSortKey(version));
    }
}
