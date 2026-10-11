using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Guards the durable split: Elsa's OpenIddict package is provider-neutral while Workbench owns its explicit
/// third-party vendor persistence choice.
/// </summary>
public sealed class OpenIddictPersistenceArchitectureTests
{
    private static readonly string[] WorkbenchOpenIddictEfPackages =
    [
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.EntityFrameworkCore.InMemory",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "MySql.EntityFrameworkCore",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "OpenIddict.EntityFrameworkCore"
    ];

    [Fact]
    public void Identity_core_and_implementation_projects_are_free_of_concrete_persistence_dependencies()
    {
        var identityRoot = Path.Combine(RepoRoot, "src", "essentials", "Foundation", "Identity");
        var implementationRoot = Path.Combine(identityRoot, "Elsa.Foundation.Identity");
        var coreFiles = Directory.EnumerateFiles(Path.Combine(identityRoot, "Core"), "*", SearchOption.AllDirectories);
        var implementationFiles = OwnProjectFiles(implementationRoot).ToArray();

        // The implementation project sits in its own folder beside its sibling sub-projects. Prove the
        // ownership filter still reaches its own project file, so it cannot pass by scanning nothing.
        Assert.Contains(Path.Combine(implementationRoot, "Elsa.Foundation.Identity.csproj"), implementationFiles);

        var violations = coreFiles
            .Concat(implementationFiles)
            .Where(IsProject)
            .SelectMany(path => ForbiddenLines(path, "EntityFrameworkCore"))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void OpenIddict_behavior_package_is_free_of_concrete_persistence_dependencies()
    {
        var root = Path.Combine(
            RepoRoot,
            "src",
            "essentials",
            "Foundation",
            "Identity",
            "OpenIddict",
            "Behavior");
        var violations = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(IsProject)
            .SelectMany(path => ForbiddenLines(path, "EntityFrameworkCore"))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    /// <summary>
    /// Elsa's reusable OpenIddict package ships no concrete persistence implementation. The only retained EF
    /// implementation is the vendor-owned model selected explicitly by Workbench.
    /// </summary>
    public void Elsa_OpenIddict_package_has_no_EF_packages_or_wrapper_sources()
    {
        var openIddictRoot = Path.Combine(RepoRoot, "src", "essentials", "Foundation", "Identity", "OpenIddict");
        var packageRoot = Path.Combine(openIddictRoot, "Elsa.Foundation.Identity.OpenIddict");
        var projectPath = Path.Combine(packageRoot, "Elsa.Foundation.Identity.OpenIddict.csproj");
        var project = XDocument.Load(projectPath);
        var efPackages = project.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include?.Contains("EntityFrameworkCore", StringComparison.Ordinal) == true)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(efPackages);

        // The behavior project is the package's sibling, never inside its folder, so the package's SDK glob cannot
        // compile the behavior sources. No project at all may sit below the package folder.
        Assert.True(File.Exists(Path.Combine(openIddictRoot, "Behavior", "Elsa.Foundation.Identity.OpenIddict.Behavior.csproj")),
            "The OpenIddict behavior project must sit beside the package folder.");
        var nestedProjects = Directory.EnumerateFiles(packageRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, projectPath, StringComparison.Ordinal))
            .ToArray();
        Assert.True(nestedProjects.Length == 0,
            "No project may sit inside the OpenIddict package folder, or its sources compile into the package: " + string.Join(", ", nestedProjects));

        // No EF wrapper, neither inside the package folder nor as a sibling project of it.
        Assert.False(Directory.Exists(Path.Combine(packageRoot, "EntityFrameworkCore")));
        Assert.False(Directory.Exists(Path.Combine(openIddictRoot, "EntityFrameworkCore")));
    }

    [Fact]
    public void Workbench_owns_the_vendor_package()
    {
        var project = XDocument.Load(Path.Combine(
            RepoRoot,
            "src",
            "apps",
            "Elsa.Workbench",
            "Elsa.Workbench.csproj"));

        Assert.Contains(
            project.Descendants("PackageReference"),
            element => string.Equals((string?)element.Attribute("Include"), "OpenIddict.EntityFrameworkCore", StringComparison.Ordinal));
        var vendorPackages = project.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include?.Contains("EntityFrameworkCore", StringComparison.Ordinal) == true)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(WorkbenchOpenIddictEfPackages, vendorPackages);

        var testProject = XDocument.Load(Path.Combine(
            RepoRoot,
            "tests",
            "essentials",
            "Foundation",
            "Identity",
            "Tests",
            "Elsa.Foundation.Identity.Tests.csproj"));
        var testVendorPackages = testProject.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .OfType<string>()
            .Where(include => include.Contains("EntityFrameworkCore", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ["Microsoft.EntityFrameworkCore.InMemory", "Microsoft.EntityFrameworkCore.Sqlite", "OpenIddict.EntityFrameworkCore"],
            testVendorPackages);
    }

    private static IEnumerable<string> OwnProjectFiles(string projectDirectory)
    {
        var pending = new Stack<string>([projectDirectory]);
        while (pending.TryPop(out var directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory))
                yield return file;

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!Directory.EnumerateFiles(child, "*.csproj").Any())
                    pending.Push(child);
            }
        }
    }

    private static bool IsProject(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ForbiddenLines(string path, params string[] tokens)
    {
        var relativePath = Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        return File.ReadLines(path)
            .Select((line, index) => (line, number: index + 1))
            .Where(candidate => tokens.Any(token => candidate.line.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Select(candidate => $"{relativePath}:{candidate.number}: {candidate.line.Trim()}");
    }
}
