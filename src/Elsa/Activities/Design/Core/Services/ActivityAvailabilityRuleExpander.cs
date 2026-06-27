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
}

internal sealed record ActivityAvailabilityRuleExpansion(
    HashSet<string> ActivityTypeKeys,
    IReadOnlyCollection<string> UnresolvedSets);
