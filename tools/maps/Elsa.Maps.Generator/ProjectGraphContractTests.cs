namespace Elsa.Maps.Generator;

/// <summary>
/// Contract checks for the one project-discovery rule the maps generator and the versioning calculator share
/// (<see cref="RepoLayout.IsProjectPath"/>, spec 259): it selects what the project pathspecs select on the live tree, and
/// it keeps Studio's nested trees out where git's pathspec wildcard would let them in.
/// </summary>
public static class ProjectGraphContractTests
{
    private static readonly (string Path, bool Project)[] Cases =
    [
        ("Essentials/src/Workflows/Elsa.Workflows.csproj", true),
        ("Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj", true),
        ("Essentials/tests/A/B/X.csproj", true),
        ("Extensions/A/src/X.csproj", true),
        ("Extensions/A/src/Sub/X.csproj", true),
        ("Extensions/A/tests/Sub/X.csproj", true),
        ("Extensions/A/studio/X.csproj", false),
        // git's pathspec "Extensions/*/src/*.csproj" matches this one, since its '*' also matches '/'.
        ("Extensions/A/studio/src/X.csproj", false),
        ("Extensions/A/X.csproj", false),
        ("Studio/src/X.csproj", false),
        ("tools/x/X.csproj", false),
        ("Essentials/src/Workflows/README.md", false)
    ];

    public static void Run()
    {
        var wrong = Cases.Where(@case => RepoLayout.IsProjectPath(@case.Path) != @case.Project)
            .Select(@case => $"{@case.Path} should {(@case.Project ? string.Empty : "not ")}be a project")
            .ToArray();
        if (wrong.Length > 0)
            throw new InvalidOperationException($"RepoLayout.IsProjectPath disagrees with the layout rule: {string.Join("; ", wrong)}.");

        // Equal today, so the shared rule changed no map. A difference names a project on a path only one of them takes.
        var repo = RepoContext.Discover();
        var byPathspec = repo.ListFiles(RepoLayout.ProjectPathspecs).ToHashSet(StringComparer.Ordinal);
        var byRule = repo.ProjectFiles().ToHashSet(StringComparer.Ordinal);
        if (byRule.Count == 0 || !byRule.SetEquals(byPathspec))
            throw new InvalidOperationException(
                "The project pathspecs and RepoLayout.IsProjectPath select different projects. Only the pathspecs: " +
                $"{string.Join(", ", byPathspec.Except(byRule).Order(StringComparer.Ordinal))}. Only the rule: " +
                $"{string.Join(", ", byRule.Except(byPathspec).Order(StringComparer.Ordinal))}. Move the project under a backend root.");

        Console.WriteLine($"Project discovery contract passed: {Cases.Length} path cases, {byRule.Count} projects on the live tree.");
    }
}
