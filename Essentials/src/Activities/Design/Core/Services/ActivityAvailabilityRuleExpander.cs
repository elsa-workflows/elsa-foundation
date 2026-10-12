using Elsa.Activities.Design.Core.Options;

namespace Elsa.Activities.Design.Core.Services;

internal static class ActivityAvailabilityRuleExpander
{
    public static ActivityAvailabilityRuleExpansion Expand(ActivityAvailabilityRuleSet? rules, IDictionary<string, string[]>? sets)
    {
        var activityTypeKeys = new HashSet<string>(StringComparer.Ordinal);
        var unresolvedSets = new List<string>();

        if (rules is null)
            return new ActivityAvailabilityRuleExpansion(activityTypeKeys, []);

        foreach (var activityType in (rules.ActivityTypes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)))
            activityTypeKeys.Add(activityType);

        foreach (var setName in (rules.Sets ?? []).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (sets is null || !sets.TryGetValue(setName, out var activityTypes))
            {
                unresolvedSets.Add(setName);
                continue;
            }

            foreach (var activityType in (activityTypes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)))
                activityTypeKeys.Add(activityType);
        }

        return new ActivityAvailabilityRuleExpansion(activityTypeKeys, unresolvedSets.ToArray());
    }

    public static HashSet<string> ResolveCatalogKeys(ActivityAvailabilityRuleExpansion expansion, HashSet<string> catalogKeys) =>
        expansion.ActivityTypeKeys
            .Where(catalogKeys.Contains)
            .ToHashSet(StringComparer.Ordinal);

    public static bool HasOnlyUnresolvedRules(
        ActivityAvailabilityRuleSet? rules,
        HashSet<string> resolvedKeys,
        IDictionary<string, string[]>? sets)
    {
        if (resolvedKeys.Count > 0)
            return false;

        var activityTypeRules = (rules?.ActivityTypes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var setRules = (rules?.Sets ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (activityTypeRules.Length == 0 && setRules.Length == 0)
            return false;

        return !setRules.Any(setName => sets?.ContainsKey(setName) == true);
    }
}

internal sealed record ActivityAvailabilityRuleExpansion(
    HashSet<string> ActivityTypeKeys,
    IReadOnlyCollection<string> UnresolvedSets);
