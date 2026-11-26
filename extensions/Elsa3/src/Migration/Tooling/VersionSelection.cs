using System.Text.Json;
using Elsa3.Migration.Tooling.Report;
using Elsa3.Models;

namespace Elsa3.Migration.Tooling;

/// <summary>What <see cref="VersionSelection"/> chose.</summary>
/// <param name="Selected">The inputs that convert, in input order.</param>
/// <param name="NotConverted">The versions selection skipped.</param>
/// <param name="NoPublishedVersion">The source version ids of definitions that convert their latest version because none is published.</param>
internal sealed record VersionSelectionResult(
    IReadOnlyList<ExportItem> Selected,
    IReadOnlyList<ReportVersionReference> NotConverted,
    IReadOnlyList<string> NoPublishedVersion);

/// <summary>
/// Chooses the versions that convert (spec 1237 FR-012): per definition the latest published version, or the latest
/// version when none is published; plus, transitively, every version a reusable reference of a selected version pins.
/// <c>--all-versions</c> selects everything. An input that shares a selected version id, or a selected definition id and
/// version, is kept too, so duplicates still reach the duplicate-input refusal of the shared import.
/// </summary>
internal static class VersionSelection
{
    // As the collection analyzer: these start another workflow by id; they are not reusable references (spec 246).
    private static readonly HashSet<string> DispatchTypes = new(StringComparer.Ordinal)
    {
        "Elsa.DispatchWorkflow",
        "Elsa.ExecuteWorkflow",
        "Elsa.BulkDispatchWorkflows"
    };

    public static VersionSelectionResult Select(IReadOnlyList<ExportItem> items, bool allVersions)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (allVersions)
            return new(items, [], []);

        var byDefinition = items
            .GroupBy(item => item.Definition.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var byVersionId = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Definition.Id))
            .GroupBy(item => item.Definition.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var selected = new HashSet<ExportItem>(ReferenceEqualityComparer.Instance);
        var noPublished = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<ExportItem>();

        foreach (var (_, versions) in byDefinition.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var published = versions.Where(item => item.Definition.IsPublished).ToArray();
            var pool = published.Length > 0 ? published : versions;
            var latest = pool.MaxBy(item => item.Definition.Version)!;
            if (published.Length == 0)
                noPublished.Add(latest.Definition.Id);
            pending.Push(latest);
        }

        while (pending.TryPop(out var item))
        {
            if (!selected.Add(item))
                continue;

            foreach (var pinned in PinnedTargets(item.Definition, byDefinition, byVersionId))
                pending.Push(pinned);
        }

        // Duplicates of a selected version id, or definition id and version, stay in so the import refuses them.
        var selectedVersionIds = selected.Select(item => item.Definition.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        var selectedVersions = selected.Select(item => (item.Definition.DefinitionId, item.Definition.Version)).ToHashSet();
        var result = items
            .Where(item => selected.Contains(item)
                           || (!string.IsNullOrWhiteSpace(item.Definition.Id) && selectedVersionIds.Contains(item.Definition.Id))
                           || selectedVersions.Contains((item.Definition.DefinitionId, item.Definition.Version)))
            .ToArray();
        var kept = result.ToHashSet(ReferenceEqualityComparer.Instance);

        var notConverted = items
            .Where(item => !kept.Contains(item))
            .Select(item => new ReportVersionReference(item.Definition.DefinitionId, item.Definition.Version, item.Definition.Id ?? string.Empty))
            .ToArray();

        return new(result, notConverted, [.. noPublished]);
    }

    /// <summary>The finding a definition with no published version carries.</summary>
    public static CollectedFinding NoPublishedVersionFinding() =>
        new(Elsa3MigrationToolingCodes.NoPublishedVersion, FindingSeverity.Info, FindingStage.Input, null, null,
            Elsa3MigrationToolingCodes.NoPublishedVersion, null);

    /// <summary>
    /// The versions a definition's reusable references pin, read and resolved as the collection analyzer does: only
    /// reusable (<c>UsableAsActivity</c>) targets; an exact <c>workflowDefinitionVersionId</c>, which must agree with a
    /// <c>workflowDefinitionId</c> when both are given; otherwise a definition id (from <c>workflowDefinitionId</c> or the
    /// activity type) with the activity's version. A reference the analyzer would refuse pins nothing, so the refusal
    /// stays the import's. A reference without a version resolves to the selected version, which is already in.
    /// </summary>
    private static IEnumerable<ExportItem> PinnedTargets(
        Elsa3WorkflowDefinition definition,
        IReadOnlyDictionary<string, ExportItem[]> byDefinition,
        IReadOnlyDictionary<string, ExportItem> byVersionId)
    {
        foreach (var activity in EnumerateActivities(definition.Root))
        {
            if (DispatchTypes.Contains(activity.Type))
                continue;

            var versionId = ReadString(activity.AdditionalProperties, "workflowDefinitionVersionId");
            var definitionId = ReadString(activity.AdditionalProperties, "workflowDefinitionId");
            if (!string.IsNullOrWhiteSpace(versionId))
            {
                if (byVersionId.TryGetValue(versionId, out var exact) && IsReusable(exact)
                    && (string.IsNullOrWhiteSpace(definitionId) || StringComparer.Ordinal.Equals(definitionId, exact.Definition.DefinitionId)))
                    yield return exact;
                continue;
            }

            if (string.IsNullOrWhiteSpace(definitionId) && byDefinition.TryGetValue(activity.Type, out var typed) && typed.Any(IsReusable))
                definitionId = activity.Type;

            if (string.IsNullOrWhiteSpace(definitionId) || activity.Version is not { } version || !byDefinition.TryGetValue(definitionId, out var versions))
                continue;

            var matches = versions.Where(item => IsReusable(item) && item.Definition.Version == version).ToArray();
            if (matches.Length == 1)
                yield return matches[0];
        }
    }

    private static bool IsReusable(ExportItem item) => item.Definition.Options?.UsableAsActivity == true;

    private static IEnumerable<Elsa3Activity> EnumerateActivities(Elsa3Activity? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<Elsa3Activity>();
        stack.Push(root);
        while (stack.TryPop(out var current))
        {
            yield return current;
            foreach (var child in current.Activities ?? [])
                stack.Push(child);
        }
    }

    private static string? ReadString(IDictionary<string, JsonElement>? properties, string name)
    {
        if (properties is null || !properties.TryGetValue(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();
        if (value.ValueKind != JsonValueKind.Object)
            return null;
        if (value.TryGetProperty("expression", out var expression) && expression.ValueKind == JsonValueKind.Object &&
            expression.TryGetProperty("value", out var expressionValue) && expressionValue.ValueKind == JsonValueKind.String)
            return expressionValue.GetString();
        return value.TryGetProperty("value", out var direct) && direct.ValueKind == JsonValueKind.String ? direct.GetString() : null;
    }
}
