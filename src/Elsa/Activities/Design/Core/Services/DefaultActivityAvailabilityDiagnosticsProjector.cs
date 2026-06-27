using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;

namespace Elsa.Activities.Design.Core.Services;

public sealed class DefaultActivityAvailabilityDiagnosticsProjector : IActivityAvailabilityDiagnosticsProjector
{
    public ActivityAvailabilityDiagnostics Project(
        IEnumerable<IActivityDefinition> activities,
        ActivityAvailabilityOptions options,
        ActivityAvailabilitySettings? managementSettings = null)
    {
        var activityList = activities.ToArray();
        var activityKeys = activityList.Select(x => x.ActivityTypeKey).ToHashSet(StringComparer.Ordinal);
        var hostInclude = ActivityAvailabilityRuleExpander.Expand(options.Include, options.Sets);
        var hostExclude = ActivityAvailabilityRuleExpander.Expand(options.Exclude, options.Sets);
        var management = ActivityAvailabilityRuleExpander.Expand(managementSettings?.Rules, options.Sets);
        var hasHostIncludeRules = hostInclude.ActivityTypeKeys.Count > 0;
        var managementHasOnlyUnresolvedSetRules = HasOnlyUnresolvedSetRules(managementSettings?.Rules, management);
        var entries = new List<ActivityAvailabilityDiagnosticEntry>();

        foreach (var activity in activityList)
            entries.Add(ProjectActivity(activity, hostInclude.ActivityTypeKeys, hostExclude.ActivityTypeKeys, hasHostIncludeRules, managementSettings, management.ActivityTypeKeys, managementHasOnlyUnresolvedSetRules));

        AddMissingActivityTypeReferences(entries, options.Include, activityKeys, ActivityAvailabilityPolicyLayer.HostBaseline);
        AddMissingActivityTypeReferences(entries, options.Exclude, activityKeys, ActivityAvailabilityPolicyLayer.HostBaseline);
        AddMissingActivityTypeReferences(entries, managementSettings?.Rules, activityKeys, ActivityAvailabilityPolicyLayer.ManagementSettings);
        AddUnresolvedSetReferences(entries, hostInclude.UnresolvedSets, ActivityAvailabilityPolicyLayer.HostBaseline);
        AddUnresolvedSetReferences(entries, hostExclude.UnresolvedSets, ActivityAvailabilityPolicyLayer.HostBaseline);
        AddUnresolvedSetReferences(entries, management.UnresolvedSets, ActivityAvailabilityPolicyLayer.ManagementSettings);

        var sets = (options.Sets ?? new Dictionary<string, string[]>(StringComparer.Ordinal))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new ActivityAvailabilitySetDiagnostic(x.Key, x.Value ?? []))
            .ToArray();

        return new ActivityAvailabilityDiagnostics(entries, sets);
    }

    private static ActivityAvailabilityDiagnosticEntry ProjectActivity(
        IActivityDefinition activity,
        HashSet<string> hostIncludeKeys,
        HashSet<string> hostExcludeKeys,
        bool hasHostIncludeRules,
        ActivityAvailabilitySettings? managementSettings,
        HashSet<string> managementKeys,
        bool managementHasOnlyUnresolvedSetRules)
    {
        if ((hasHostIncludeRules && !hostIncludeKeys.Contains(activity.ActivityTypeKey)) || hostExcludeKeys.Contains(activity.ActivityTypeKey))
            return Entry(activity, ActivityAvailabilityDiagnosticState.BlockedByHostBaseline, ActivityAvailabilityPolicyLayer.HostBaseline, "Blocked by host activity availability baseline.");

        if (managementSettings is not null && IsHiddenByManagement(activity.ActivityTypeKey, managementSettings.Mode, managementKeys, managementHasOnlyUnresolvedSetRules))
            return Entry(activity, ActivityAvailabilityDiagnosticState.HiddenByManagementSettings, ActivityAvailabilityPolicyLayer.ManagementSettings, "Hidden by management activity availability settings.");

        return Entry(activity, ActivityAvailabilityDiagnosticState.Available, ActivityAvailabilityPolicyLayer.Catalog, "Available for new workflow authoring.");
    }

    private static bool IsHiddenByManagement(string activityTypeKey, ActivityAvailabilityManagementMode mode, HashSet<string> managementKeys, bool onlyUnresolvedSetRules) =>
        mode switch
        {
            ActivityAvailabilityManagementMode.AllExcept => managementKeys.Contains(activityTypeKey),
            ActivityAvailabilityManagementMode.Only when !onlyUnresolvedSetRules => !managementKeys.Contains(activityTypeKey),
            _ => false
        };

    private static bool HasOnlyUnresolvedSetRules(ActivityAvailabilityRuleSet? rules, ActivityAvailabilityRuleExpansion expansion) =>
        expansion.ActivityTypeKeys.Count == 0
        && expansion.UnresolvedSets.Count > 0
        && (rules?.ActivityTypes ?? []).All(string.IsNullOrWhiteSpace);

    private static ActivityAvailabilityDiagnosticEntry Entry(
        IActivityDefinition activity,
        ActivityAvailabilityDiagnosticState state,
        ActivityAvailabilityPolicyLayer layer,
        string reason) =>
        new(activity.Id, activity.ActivityTypeKey, activity.DisplayName, activity.Category, state, layer, ActivityAvailabilityReferenceKind.ActivityType, activity.ActivityTypeKey, reason);

    private static void AddMissingActivityTypeReferences(
        ICollection<ActivityAvailabilityDiagnosticEntry> entries,
        ActivityAvailabilityRuleSet? rules,
        HashSet<string> activityKeys,
        ActivityAvailabilityPolicyLayer layer)
    {
        foreach (var activityTypeKey in (rules?.ActivityTypes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
        {
            if (activityKeys.Contains(activityTypeKey))
                continue;

            entries.Add(new ActivityAvailabilityDiagnosticEntry(
                null,
                activityTypeKey,
                null,
                null,
                ActivityAvailabilityDiagnosticState.RemovedFromCatalog,
                layer,
                ActivityAvailabilityReferenceKind.ActivityType,
                activityTypeKey,
                "Referenced activity type key is not present in the activity catalog."));
        }
    }

    private static void AddUnresolvedSetReferences(
        ICollection<ActivityAvailabilityDiagnosticEntry> entries,
        IEnumerable<string> setNames,
        ActivityAvailabilityPolicyLayer layer)
    {
        foreach (var setName in setNames.Distinct(StringComparer.Ordinal))
            entries.Add(new ActivityAvailabilityDiagnosticEntry(
                null,
                null,
                null,
                null,
                ActivityAvailabilityDiagnosticState.UnresolvedReference,
                layer,
                ActivityAvailabilityReferenceKind.ActivitySet,
                setName,
                "Referenced activity set is not defined by host configuration."));
    }
}
