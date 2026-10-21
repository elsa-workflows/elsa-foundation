using System.Text;

namespace Elsa.Maps.Generator;

/// <summary>
/// Writes <c>docs/maps/dependency-map.json</c>, the canonical backend dependency map (spec 149): every project under
/// the Essentials and extension backend roots as a node, keyed by path, with its typed dependency edges.
/// </summary>
/// <remarks>
/// <para>
/// This is the dataset the markdown maps describing the project graph are projections of. It is not committed
/// (spec 259): the versioning calculator derives it from each commit's own tree with the same scan
/// (<see cref="DependencyMapJson"/>), and CI publishes it with the other maps on the <c>generated-maps</c> branch. It holds
/// no publish state — the last-published record lives on the <c>publish-state</c> branch (spec 149 FR-011) — and no
/// source-file inventory (FR-009), so it changes only when the project graph does.
/// </para>
/// <para>
/// Every edge but one kind comes from the project files. A packable node's pinned-transitive edges — the packages its
/// nuspec lists because central package management pins them for it transitively — come from the NuGet lock file
/// beside it (<see cref="PinnedTransitiveDependencies"/>), so generating this map needs no restore.
/// </para>
/// </remarks>
public static class DependencyMap
{
    /// <summary>The dataset's repo-relative path.</summary>
    public const string RelativePath = DependencyMapJson.RelativePath;

    /// <summary>Writes the dataset and returns the repo-relative path written.</summary>
    public static IReadOnlyList<string> Generate(RepoContext repo, IReadOnlyList<ProjectFacts> projects)
    {
        MarkdownTable.Write(repo.Absolute(RelativePath), new StringBuilder(DependencyMapJson.Serialize(repo, projects)));
        return [RelativePath];
    }
}
