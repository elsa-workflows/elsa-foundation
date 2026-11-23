namespace Elsa.Maps.Generator;

/// <summary>Contract checks that the backend map inventory covers its product roots and excludes Studio.</summary>
public static class RepoLayoutContractTests
{
    public static void Run()
    {
        var repo = RepoContext.Discover();
        var roots = new[] { "essentials/src", "apps", "essentials/tests" }
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

        var studioProjects = Directory.Exists(repo.Absolute("extensions"))
            ? Directory.EnumerateFiles(repo.Absolute("extensions"), "*.csproj", SearchOption.AllDirectories)
                .Where(path => Path.GetRelativePath(repo.Root, path).Replace('\\', '/').Contains("/studio/", StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(repo.Root, path).Replace('\\', '/'))
                .ToArray()
            : [];
        var includedStudio = actualProjects.Where(path => path.Contains("/studio/", StringComparison.Ordinal)).ToArray();
        if (studioProjects.Length == 0 || includedStudio.Length > 0)
            throw new InvalidOperationException(
                "Studio exclusion is not proven: the repository must contain Studio projects and no Studio project may enter backend maps. " +
                $"Found {studioProjects.Length} Studio projects and included {includedStudio.Length}.");

        var templateProjects = Directory.Exists(repo.Absolute("extensions"))
            ? Directory.EnumerateFiles(repo.Absolute("extensions"), "*.csproj", SearchOption.AllDirectories)
                .Where(path => Path.GetRelativePath(repo.Root, path).Replace('\\', '/').Contains("/templates/", StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(repo.Root, path).Replace('\\', '/'))
                .ToArray()
            : [];
        var includedTemplates = actualProjects.Where(path => path.Contains("/templates/", StringComparison.Ordinal)).ToArray();
        if (templateProjects.Length == 0 || includedTemplates.Length > 0)
            throw new InvalidOperationException(
                "dotnet-new template exclusion is not proven: the repository must contain scaffold .csproj files under " +
                "extensions/<capability>/templates/ and none of them may enter backend maps. " +
                $"Found {templateProjects.Length} template projects and included {includedTemplates.Length}.");

        var sourceFiles = RepoLayout.SourceFiles(repo);
        if (!sourceFiles.Any(path => path.StartsWith("essentials/src/", StringComparison.Ordinal)) ||
            !sourceFiles.Any(path => path.StartsWith("apps/", StringComparison.Ordinal)) ||
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

        if (!RepoLayout.IsTestPath("essentials/tests/Architecture/Tests/Example.cs") ||
            !RepoLayout.IsTestProjectPath("extensions/Weaver/tests/Tests/Example.csproj") ||
            !RepoLayout.IsTestPath("extensions/Agent/src/tests/Example.cs") ||
            RepoLayout.IsModulePath("extensions/Agent/src/tests/Example.cs") ||
            RepoLayout.IsModulePath("essentials/tests/Architecture/Tests/Example.cs") ||
            RepoLayout.IsModulePath("extensions/Weaver/tests/Tests/Example.cs") ||
            RepoLayout.IsTestProjectPath("extensions/Agent/studio/tests/Example.Tests.csproj") ||
            RepoLayout.IsModulePath("extensions/Agent/studio/Elsa.Studio.Agent/Example.cs"))
            throw new InvalidOperationException("Backend path classification does not preserve test segments and Studio exclusion.");

        Console.WriteLine($"Backend path inventory passed: {actualProjects.Count} projects, {studioProjects.Length} Studio projects excluded.");
    }

    private static IEnumerable<string> ExtensionRoots(string repositoryRoot, params string[] subroots) =>
        Directory.Exists(Path.Join(repositoryRoot, "extensions"))
            ? Directory.EnumerateDirectories(Path.Join(repositoryRoot, "extensions"))
                .SelectMany(extension => subroots.Select(subroot =>
                    $"extensions/{Path.GetFileName(extension)}/{subroot}"))
            : [];
}
