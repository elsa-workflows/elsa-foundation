namespace Elsa.Maps.Generator;

/// <summary>
/// Reports whether the generated files committed to <c>main</c> still describe the tree: only the
/// <c>Elsa.Server.*.slnf</c> solution filters are left.
/// </summary>
/// <remarks>
/// <para>
/// The documentation maps, their manifest and the findings reports are not committed (spec 257), and neither is
/// <c>docs/maps/dependency-map.json</c> (spec 259): the versioning calculator derives the dataset from each commit's own
/// tree with the scan in <c>tools/maps/Elsa.Maps.Projects</c>, so there is no copy to hold fresh. A tree that scan cannot
/// read fails <c>DependencyMapTests</c> in <c>Build &amp; test</c> instead.
/// </para>
/// <para>
/// So <c>check</c> is the solution-filter check, the same as <c>solution-filters-check</c>, kept under its old name so
/// "run <c>-- check</c>" keeps meaning "check what generated output is committed".
/// </para>
/// </remarks>
public static class MapFreshness
{
    /// <summary>Returns 0 when the committed solution filters match freshly generated ones, 1 otherwise.</summary>
    public static int Check(RepoContext repo) => SolutionFilterGenerator.Check(repo);
}
