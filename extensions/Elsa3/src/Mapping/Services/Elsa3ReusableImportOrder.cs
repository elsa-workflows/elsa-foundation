using Elsa3.Activities.Design.Import.Models;

namespace Elsa3.Mapping.Services;

/// <summary>
/// The order selected reusable versions are converted and published in (spec 246 R-5): every version after the
/// versions it uses, and the versions of one definition in ascending order, because the publisher compiles against
/// published dependencies and only publishes above the current head. Ties go to the lower source version id.
/// </summary>
internal static class Elsa3ReusableImportOrder
{
    /// <summary>Orders the reusable items, or returns the ids that cannot be ordered because they wait on each other.</summary>
    public static IReadOnlyList<ReusableActivityImportItem> Order(
        IReadOnlyCollection<ReusableActivityImportItem> reusable,
        out IReadOnlyList<string> unordered)
    {
        var byId = reusable.ToDictionary(item => item.SourceVersionId, StringComparer.Ordinal);
        var before = byId.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var item in reusable)
        {
            foreach (var dependency in item.Dependencies.Where(dependency => byId.ContainsKey(dependency.TargetSourceVersionId)))
            {
                if (!StringComparer.Ordinal.Equals(dependency.TargetSourceVersionId, item.SourceVersionId))
                    before[item.SourceVersionId].Add(dependency.TargetSourceVersionId);
            }
        }

        foreach (var versions in reusable.GroupBy(item => item.SourceDefinitionId, StringComparer.Ordinal))
        {
            var ascending = versions.OrderBy(item => item.SourceVersion).ThenBy(item => item.SourceVersionId, StringComparer.Ordinal).ToArray();
            for (var index = 1; index < ascending.Length; index++)
                before[ascending[index].SourceVersionId].Add(ascending[index - 1].SourceVersionId);
        }

        var ordered = new List<ReusableActivityImportItem>(reusable.Count);
        var ready = new SortedSet<string>(before.Where(entry => entry.Value.Count == 0).Select(entry => entry.Key), StringComparer.Ordinal);
        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            ordered.Add(byId[next]);
            foreach (var (id, waitsOn) in before)
            {
                if (waitsOn.Remove(next) && waitsOn.Count == 0)
                    ready.Add(id);
            }
        }

        unordered = before.Where(entry => entry.Value.Count > 0).Select(entry => entry.Key).Order(StringComparer.Ordinal).ToArray();
        return ordered;
    }
}
