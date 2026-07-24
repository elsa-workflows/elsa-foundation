using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class OpenIddictPersistenceArchitectureTests
{
    private static readonly string[] TransitionalOpenIddictEfPackages =
    [
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.EntityFrameworkCore.InMemory",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "OpenIddict.EntityFrameworkCore"
    ];

    [Fact]
    public void Identity_abstractions_are_free_of_concrete_persistence_dependencies()
    {
        var root = Path.Combine(RepoRoot, "src", "Elsa", "Foundation", "Identity", "Abstractions");
        var violations = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(IsSourceOrProject)
            .SelectMany(path => ForbiddenLines(path, "Groundwork", "EntityFrameworkCore"))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void OpenIddict_behavior_package_is_free_of_concrete_persistence_dependencies()
    {
        var root = Path.Combine(
            RepoRoot,
            "src",
            "Elsa",
            "Foundation",
            "Identity",
            "OpenIddict",
            "Behavior");
        var violations = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(IsSourceOrProject)
            .SelectMany(path => ForbiddenLines(path, "Groundwork", "EntityFrameworkCore"))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Groundwork_references_behavior_without_reaching_the_EF_oracle()
    {
        var projectPath = Path.Combine(
            RepoRoot,
            "src",
            "Elsa",
            "Foundation",
            "Identity",
            "OpenIddict",
            "Groundwork",
            "Elsa.Foundation.Identity.OpenIddict.Groundwork.csproj");
        var project = XDocument.Load(projectPath);
        var references = project.Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .ToArray();

        Assert.Contains(
            references,
            include => include?.EndsWith(
                @"Behavior\Elsa.Foundation.Identity.OpenIddict.Behavior.csproj",
                StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            references,
            include => include?.EndsWith(
                @"Elsa.Foundation.Identity.OpenIddict.csproj",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Transitional_EF_oracle_keeps_the_frozen_packages_and_excludes_nested_sources()
    {
        var projectPath = Path.Combine(
            RepoRoot,
            "src",
            "Elsa",
            "Foundation",
            "Identity",
            "OpenIddict",
            "Elsa.Foundation.Identity.OpenIddict.csproj");
        var project = XDocument.Load(projectPath);
        var efPackages = project.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include?.Contains("EntityFrameworkCore", StringComparison.Ordinal) == true)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(TransitionalOpenIddictEfPackages, efPackages);
        Assert.Contains(
            project.Descendants("Compile"),
            element =>
                string.Equals((string?)element.Attribute("Remove"), "Groundwork/**/*.cs", StringComparison.Ordinal));
        Assert.Contains(
            project.Descendants("Compile"),
            element =>
                string.Equals((string?)element.Attribute("Remove"), "Behavior/**/*.cs", StringComparison.Ordinal));
    }

    private static bool IsSourceOrProject(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ForbiddenLines(string path, params string[] tokens)
    {
        var relativePath = Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        return File.ReadLines(path)
            .Select((line, index) => (line, number: index + 1))
            .Where(candidate => tokens.Any(token =>
                candidate.line.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Select(candidate => $"{relativePath}:{candidate.number}: {candidate.line.Trim()}");
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }
}
