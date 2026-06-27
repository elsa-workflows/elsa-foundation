using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;

namespace Elsa.Activities.Design.Core.Services;

public sealed class DefaultActivityAvailabilityEvaluator(ActivityAvailabilityOptions options) : IActivityAvailabilityEvaluator
{
    public IReadOnlyCollection<IActivityDefinition> FilterAddable(IEnumerable<IActivityDefinition> activities, ActivityAvailabilitySettings? managementSettings = null)
    {
        var activityList = activities.ToArray();
        var includeKeys = ActivityAvailabilityRuleExpander.Expand(options.Include, options.Sets).ActivityTypeKeys;
        var excludeKeys = ActivityAvailabilityRuleExpander.Expand(options.Exclude, options.Sets).ActivityTypeKeys;
        var hasIncludeRules = includeKeys.Count > 0;

        var baselineActivities = activityList
            .Where(activity => (!hasIncludeRules || includeKeys.Contains(activity.ActivityTypeKey))
                && !excludeKeys.Contains(activity.ActivityTypeKey))
            .ToArray();

        if (managementSettings is null)
            return baselineActivities;

        var management = ActivityAvailabilityRuleExpander.Expand(managementSettings.Rules, options.Sets);
        var managementKeys = management.ActivityTypeKeys;
        var onlyUnresolvedSetRules = HasOnlyUnresolvedSetRules(managementSettings.Rules, management);

        return managementSettings.Mode switch
        {
            ActivityAvailabilityManagementMode.AllExcept => baselineActivities
                .Where(activity => !managementKeys.Contains(activity.ActivityTypeKey))
                .ToArray(),
            ActivityAvailabilityManagementMode.Only when !onlyUnresolvedSetRules => baselineActivities
                .Where(activity => managementKeys.Contains(activity.ActivityTypeKey))
                .ToArray(),
            _ => baselineActivities
        };
    }

    private static bool HasOnlyUnresolvedSetRules(ActivityAvailabilityRuleSet? rules, ActivityAvailabilityRuleExpansion expansion) =>
        expansion.ActivityTypeKeys.Count == 0
        && expansion.UnresolvedSets.Count > 0
        && (rules?.ActivityTypes ?? []).All(string.IsNullOrWhiteSpace);
}
