namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// The one order every command reports and every artifact is numbered by: topological on
/// <see cref="EfModuleDescriptor.DependsOn"/>, tie-broken by ordinal name (FR-041). Never input order,
/// never discovery order — a DBA applies <c>NN-&lt;slug&gt;.sql</c> files in the order they are named, so
/// the same selection must number the same way however it was typed.
/// </summary>
internal static class EfModuleOrder
{
    /// <summary>
    /// Orders <paramref name="selected"/>, refusing rather than guessing on the two graphs that have no
    /// order: a dependency outside the selection, and a cycle. Both are checked before any ordering is
    /// attempted, and both name every offender.
    /// </summary>
    public static IReadOnlyList<EfModuleDescriptor> Sort(IReadOnlyList<EfModuleDescriptor> selected)
    {
        var byName = selected.ToDictionary(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase);
        var dependencies = selected.ToDictionary(
            descriptor => descriptor.Name,
            Dependencies,
            StringComparer.OrdinalIgnoreCase);

        var missing = selected
            .SelectMany(descriptor => dependencies[descriptor.Name]
                .Where(dependency => !byName.ContainsKey(dependency))
                .Select(dependency => $"'{descriptor.Name}' depends on '{dependency}', which is not in the selection."))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw EfToolingRefusal.Resolution(
                "dependency-missing",
                "A selected module depends on a module that is not selected.",
                missing);
        }

        // Kahn's algorithm over an ordinal-sorted ready set: the result is a function of the graph and the
        // names alone, so the same selection in any input order produces the same numbering.
        var remaining = dependencies.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.OrdinalIgnoreCase);
        var dependents = selected.ToDictionary(descriptor => descriptor.Name, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in selected)
            foreach (var dependency in dependencies[descriptor.Name])
                dependents[dependency].Add(descriptor.Name);

        var ready = new SortedSet<string>(
            remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key),
            StringComparer.Ordinal);
        var ordered = new List<EfModuleDescriptor>(selected.Count);
        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            ordered.Add(byName[next]);
            foreach (var dependent in dependents[next])
                if (--remaining[dependent] == 0)
                    ready.Add(dependent);
        }

        if (ordered.Count != selected.Count)
        {
            var unordered = remaining.Where(pair => pair.Value > 0).Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            throw EfToolingRefusal.Resolution(
                "dependency-cycle",
                "A module dependency cycle has no order to apply migrations in.",
                [DescribeCycle(unordered, byName, dependencies)]);
        }

        return ordered;
    }

    /// <summary>Declared dependencies, deduplicated and ordinal-ordered, so a declaration's own order never reaches the artifact.</summary>
    public static IReadOnlyList<string> Dependencies(EfModuleDescriptor descriptor) =>
        [.. descriptor.DependsOn.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)];

    private static string DescribeCycle(
        HashSet<string> unordered,
        IReadOnlyDictionary<string, EfModuleDescriptor> byName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
    {
        // Walk dependencies from the ordinally-first unordered module until a name repeats: every module
        // still unordered sits on or behind a cycle, so the walk cannot escape the graph.
        var current = unordered.Order(StringComparer.Ordinal).First();
        var path = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (!seen.ContainsKey(current))
        {
            seen[current] = path.Count;
            path.Add(byName[current].Name);
            current = dependencies[current].First(unordered.Contains);
        }

        return string.Join(" -> ", path.Skip(seen[current]).Append(byName[current].Name));
    }
}
