using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;

namespace Elsa.Activities.Design.Core.Services;

public sealed class DefaultActivityAvailabilityEvaluator(ActivityAvailabilityOptions options) : IActivityAvailabilityEvaluator
{
    public IReadOnlyCollection<IActivityDefinition> FilterAddable(IEnumerable<IActivityDefinition> activities, ActivityAvailabilitySettings? managementSettings = null)
    {
        var activityList = activities.ToArray();
        var includeKeys = Expand(options.Include);
        var excludeKeys = Expand(options.Exclude);
        var hasIncludeRules = includeKeys.Count > 0;

        var baselineActivities = activityList
            .Where(activity => (!hasIncludeRules || includeKeys.Contains(activity.ActivityTypeKey))
                && !excludeKeys.Contains(activity.ActivityTypeKey))
            .ToArray();

        if (managementSettings is null)
            return baselineActivities;

        var managementKeys = Expand(managementSettings.Rules);

        return managementSettings.Mode switch
        {
            ActivityAvailabilityManagementMode.AllExcept => baselineActivities
                .Where(activity => !managementKeys.Contains(activity.ActivityTypeKey))
                .ToArray(),
            ActivityAvailabilityManagementMode.Only => baselineActivities
                .Where(activity => managementKeys.Contains(activity.ActivityTypeKey))
                .ToArray(),
            _ => baselineActivities
        };
    }

    private HashSet<string> Expand(ActivityAvailabilityRuleSet? rules)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        if (rules is null)
            return result;

        foreach (var activityType in (rules.ActivityTypes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)))
            result.Add(activityType);

        foreach (var setName in (rules.Sets ?? []).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (options.Sets is null || !options.Sets.TryGetValue(setName, out var activityTypes))
                continue;

            foreach (var activityType in (activityTypes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)))
                result.Add(activityType);
        }

        return result;
    }
}
