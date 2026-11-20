using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Models;

namespace Elsa3.Activities.Design.Import.Reconciliation.Services;

/// <summary>
/// Decides which input items one start imports (spec 235 research R-5). Each step removes the failing items and,
/// transitively, every item that uses one of them as a reusable workflow.
/// </summary>
internal sealed class Elsa3ImportSelection
{
    /// <summary>
    /// Step 1, before analysis: a blank source version id, or a source version id or definition id and version that
    /// occurs more than once. The analyzer drops these with a diagnostic that names no item, so they are reported here.
    /// </summary>
    public (IReadOnlyList<Elsa3InputItem> Kept, IReadOnlyList<Elsa3SkippedItem> Skipped) SelectInput(IReadOnlyList<Elsa3InputItem> items)
    {
        var duplicateIds = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Definition.Id))
            .GroupBy(item => item.Definition.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var duplicateVersions = items
            .GroupBy(item => (item.Definition.DefinitionId, item.Definition.Version))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        var kept = new List<Elsa3InputItem>();
        var skipped = new List<Elsa3SkippedItem>();
        foreach (var item in items)
        {
            var cause = string.IsNullOrWhiteSpace(item.Definition.Id) ? "The definition has no version id."
                : duplicateIds.Contains(item.Definition.Id) ? $"The version id '{item.Definition.Id}' occurs more than once in the input."
                : duplicateVersions.Contains((item.Definition.DefinitionId, item.Definition.Version)) ? "The definition id and version occur more than once in the input."
                : null;
            if (cause is null)
                kept.Add(item);
            else
                skipped.Add(new(Elsa3ReconciliationDiagnosticCodes.DuplicateInput, item.FilePath, item.Definition.DefinitionId, item.Definition.Version, cause));
        }

        return (kept, skipped);
    }

    /// <summary>Step 2: the plan's items with an error diagnostic, and their dependents.</summary>
    public (IReadOnlyList<string> Selected, IReadOnlyList<Elsa3SkippedItem> Skipped) SelectPlan(
        ReusableActivityImportPlan plan,
        IReadOnlyDictionary<string, Elsa3InputItem> inputs)
    {
        var failing = plan.Items
            .Where(item => !item.CanApply)
            .ToDictionary(
                item => item.SourceVersionId,
                item => item.Diagnostics.First(diagnostic => diagnostic.IsError),
                StringComparer.Ordinal);
        return Exclude(plan, failing.ToDictionary(
            entry => entry.Key,
            entry => (entry.Value.Code, Cause(entry.Value.Message, entry.Value.Guidance)),
            StringComparer.Ordinal), inputs, plan.Items.Select(item => item.SourceVersionId));
    }

    /// <summary>
    /// Removes <paramref name="failing"/> and, transitively, every item of <paramref name="candidates"/> that depends on
    /// one of them; returns what stays selected and a skip for each removed item. A dependent's cause says its reusable
    /// workflow was <paramref name="missingTargetState"/>.
    /// </summary>
    public (IReadOnlyList<string> Selected, IReadOnlyList<Elsa3SkippedItem> Skipped) Exclude(
        ReusableActivityImportPlan plan,
        IReadOnlyDictionary<string, (string Code, string Cause)> failing,
        IReadOnlyDictionary<string, Elsa3InputItem> inputs,
        IEnumerable<string> candidates,
        string missingTargetState = "not imported")
    {
        var items = plan.Items.ToDictionary(item => item.SourceVersionId, StringComparer.Ordinal);
        var remaining = candidates.Where(items.ContainsKey).ToHashSet(StringComparer.Ordinal);
        var skipped = new List<Elsa3SkippedItem>();

        foreach (var (sourceVersionId, (code, cause)) in failing.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            if (remaining.Remove(sourceVersionId))
                skipped.Add(Skip(items[sourceVersionId], inputs, code, cause));

        var blocked = failing.Keys.ToHashSet(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var sourceVersionId in remaining.Order(StringComparer.Ordinal).ToArray())
            {
                var dependency = items[sourceVersionId].Dependencies.FirstOrDefault(dependency => blocked.Contains(dependency.TargetSourceVersionId));
                if (dependency is null)
                    continue;
                remaining.Remove(sourceVersionId);
                blocked.Add(sourceVersionId);
                skipped.Add(Skip(items[sourceVersionId], inputs, Elsa3ReconciliationDiagnosticCodes.BlockedByDependency,
                    $"It uses reusable workflow '{dependency.TargetSourceDefinitionId}', which was {missingTargetState}.", dependency.TargetSourceVersionId));
                changed = true;
            }
        } while (changed);

        return (remaining.Order(StringComparer.Ordinal).ToArray(), skipped);
    }

    private static Elsa3SkippedItem Skip(
        ReusableActivityImportItem item,
        IReadOnlyDictionary<string, Elsa3InputItem> inputs,
        string code,
        string cause,
        string? blockedBy = null) =>
        new(code, inputs.TryGetValue(item.SourceVersionId, out var input) ? input.FilePath : "(unknown)",
            item.SourceDefinitionId, item.SourceVersion, cause, blockedBy);

    internal static string Cause(string message, string? guidance) =>
        string.IsNullOrWhiteSpace(guidance) ? message : $"{message} {guidance}";
}
