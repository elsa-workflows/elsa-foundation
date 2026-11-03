using Elsa.Activities.Switch.Models;
using Elsa.Workflows.Design.Core.Models;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Internal;

/// <summary>
/// Shared <c>Switch</c> case rules so the design-time validator, the compile-time structure handler, and
/// the runtime navigator agree on ordinal label uniqueness. Schema-specific checks also preserve
/// predicate identity and refuse activation-only bindings before compilation.
/// </summary>
internal static class SwitchCaseRules
{
    public static IEnumerable<string> StructureErrors(ActivityNode activity, SwitchAuthoredStructure structure)
    {
        if (activity.Structure?.SchemaVersion == SwitchActivity.LegacyStructureSchemaVersion)
        {
            if (structure.Cases.Any(item => item.Condition is not null))
                yield return "Upgrade Switch to case conditions before authoring a predicate.";
            yield break;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in structure.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.CaseId) || !ids.Add(item.CaseId))
                yield return "Each Switch case requires a unique, non-empty case identity.";
            if (string.Equals(item.Condition?.Value?.ExpressionType, "Secret", StringComparison.OrdinalIgnoreCase))
                yield return "Switch conditions cannot resolve activation-only Secret references.";
        }
        if (structure.Cases.Any(item => item.Condition is null) && activity.Inputs.Any(input =>
                input.ReferenceKey == nameof(SwitchActivity.Value) && string.Equals(input.Value?.ExpressionType, "Secret", StringComparison.OrdinalIgnoreCase)))
            yield return "A Switch with activation-only Secret matching must retain its legacy version or replace that Value binding before upgrading.";
    }

    /// <summary>
    /// Returns each match value that appears on more than one case, in first-seen order. Empty when every
    /// case has a unique match value.
    /// </summary>
    public static IEnumerable<string> DuplicateMatches(IEnumerable<string> matchValues) =>
        matchValues
            .GroupBy(match => match, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
}
