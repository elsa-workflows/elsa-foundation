namespace Elsa.Maps.Generator;

/// <summary>Contract checks that the backend map inventory covers its product roots and excludes Studio.</summary>
public static class RepoLayoutContractTests
{
    public static void Run()
    {
        var repo = RepoContext.Discover();
        var roots = new[] { "Essentials/src", "Essentials/apps", "Essentials/tests" }
            .Concat(ExtensionRoots(repo.Root, "src", "tests"))
            .Where(root => Directory.Exists(repo.Absolute(root)))
            .ToArray();
        var expectedProjects = roots
            .SelectMany(root => Directory.EnumerateFiles(repo.Absolute(root), "*.csproj", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(repo.Root, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
        var actualProjects = repo.ListFiles(RepoLayout.ProjectPathspecs).ToHashSet(StringComparer.Ordinal);

        if (expectedProjects.Count == 0 || !expectedProjects.SetEquals(actualProjects))
            throw new InvalidOperationException(
                "Backend project pathspecs do not cover the independent Essentials and Extensions src/tests inventory. " +
                $"Missing: {string.Join(", ", expectedProjects.Except(actualProjects).Order(StringComparer.Ordinal))}; " +
                $"unexpected: {string.Join(", ", actualProjects.Except(expectedProjects).Order(StringComparer.Ordinal))}.");

        var studioProjects = Directory.Exists(repo.Absolute("Extensions"))
            ? Directory.EnumerateFiles(repo.Absolute("Extensions"), "*.csproj", SearchOption.AllDirectories)
                .Where(path => Path.GetRelativePath(repo.Root, path).Replace('\\', '/').Contains("/studio/", StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(repo.Root, path).Replace('\\', '/'))
                .ToArray()
            : [];
        var includedStudio = actualProjects.Where(path => path.Contains("/studio/", StringComparison.Ordinal)).ToArray();
        if (studioProjects.Length == 0 || includedStudio.Length > 0)
            throw new InvalidOperationException(
                "Studio exclusion is not proven: the repository must contain Studio projects and no Studio project may enter backend maps. " +
                $"Found {studioProjects.Length} Studio projects and included {includedStudio.Length}.");

        var sourceFiles = RepoLayout.SourceFiles(repo);
        if (!sourceFiles.Any(path => path.StartsWith("Essentials/src/", StringComparison.Ordinal)) ||
            !sourceFiles.Any(path => path.StartsWith("Essentials/apps/", StringComparison.Ordinal)) ||
            !sourceFiles.Any(RepoLayout.IsExtensionSourcePath) ||
            sourceFiles.Any(path => !RepoLayout.IsModulePath(path) || path.Contains("/studio/", StringComparison.Ordinal)))
            throw new InvalidOperationException("Backend source pathspecs missed a product root or included a non-backend/Studio source path.");

        var expectedCatalogs = roots.Where(root => !RepoLayout.IsTestPath(root + "/"))
            .SelectMany(root => Directory.EnumerateFiles(repo.Absolute(root), "EXTENSION_POINTS.md", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(repo.Root, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
        var actualCatalogs = repo.ListFiles(RepoLayout.CatalogPathspecs).ToHashSet(StringComparer.Ordinal);
        if (expectedCatalogs.Count == 0 || !expectedCatalogs.SetEquals(actualCatalogs))
            throw new InvalidOperationException("Catalog selectors must cover exactly backend EXTENSION_POINTS.md files, never ordinary README or Studio files.");

        if (!RepoLayout.IsTestPath("Essentials/tests/Architecture/Tests/Example.cs") ||
            !RepoLayout.IsTestProjectPath("Extensions/Agent/tests/Tests/Example.csproj") ||
            !RepoLayout.IsTestPath("Extensions/Agent/src/tests/Example.cs") ||
            RepoLayout.IsModulePath("Extensions/Agent/src/tests/Example.cs") ||
            RepoLayout.IsModulePath("Essentials/tests/Architecture/Tests/Example.cs") ||
            RepoLayout.IsModulePath("Extensions/Agent/tests/Tests/Example.cs") ||
            RepoLayout.IsTestProjectPath("Extensions/Agent/studio/tests/Example.Tests.csproj") ||
            RepoLayout.IsModulePath("Extensions/Agent/studio/Elsa.Studio.Agent/Example.cs"))
            throw new InvalidOperationException("Backend path classification does not preserve test segments and Studio exclusion.");

        Console.WriteLine($"Backend path inventory passed: {actualProjects.Count} projects, {studioProjects.Length} Studio projects excluded.");
    }

    private static IEnumerable<string> ExtensionRoots(string repositoryRoot, params string[] subroots) =>
        Directory.Exists(Path.Join(repositoryRoot, "Extensions"))
            ? Directory.EnumerateDirectories(Path.Join(repositoryRoot, "Extensions"))
                .SelectMany(extension => subroots.Select(subroot =>
                    $"Extensions/{Path.GetFileName(extension)}/{subroot}"))
            : [];
}
