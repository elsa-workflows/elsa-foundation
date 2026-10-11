using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;

namespace Elsa.Activities.Design.Core.Services;

public sealed class DefaultActivityAvailabilityEvaluator(ActivityAvailabilityOptions options) : IActivityAvailabilityEvaluator
{
    public IReadOnlyCollection<IActivityDefinition> FilterAddable(IEnumerable<IActivityDefinition> activities, ActivityAvailabilitySettings? managementSettings = null)
    {
        var activityList = activities.ToArray();
        var catalogKeys = activityList.Select(activity => activity.ActivityTypeKey).ToHashSet(StringComparer.Ordinal);
        var includeKeys = ActivityAvailabilityRuleExpander.ResolveCatalogKeys(ActivityAvailabilityRuleExpander.Expand(options.Include, options.Sets), catalogKeys);
        var excludeKeys = ActivityAvailabilityRuleExpander.ResolveCatalogKeys(ActivityAvailabilityRuleExpander.Expand(options.Exclude, options.Sets), catalogKeys);
        var hasIncludeRules = includeKeys.Count > 0;

        var baselineActivities = activityList
            .Where(activity => (!hasIncludeRules || includeKeys.Contains(activity.ActivityTypeKey))
                && !excludeKeys.Contains(activity.ActivityTypeKey))
            .ToArray();

        if (managementSettings is null)
            return baselineActivities;

        var management = ActivityAvailabilityRuleExpander.Expand(managementSettings.Rules, options.Sets);
        var managementKeys = ActivityAvailabilityRuleExpander.ResolveCatalogKeys(management, catalogKeys);
        var onlyUnresolvedRules = ActivityAvailabilityRuleExpander.HasOnlyUnresolvedRules(managementSettings.Rules, managementKeys, options.Sets);

        return managementSettings.Mode switch
        {
            ActivityAvailabilityManagementMode.AllExcept => baselineActivities
                .Where(activity => !managementKeys.Contains(activity.ActivityTypeKey))
                .ToArray(),
            ActivityAvailabilityManagementMode.Only when !onlyUnresolvedRules => baselineActivities
                .Where(activity => managementKeys.Contains(activity.ActivityTypeKey))
                .ToArray(),
            _ => baselineActivities
        };
    }

}
