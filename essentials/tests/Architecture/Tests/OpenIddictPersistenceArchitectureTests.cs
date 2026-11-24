using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Guards the durable split: Elsa's OpenIddict feature package is provider-neutral, and the third-party vendor EF store is
/// one opt-in, host-composed package beside it that hosts select explicitly (owner decision 2026-11-24).
/// </summary>
public sealed class OpenIddictPersistenceArchitectureTests
{
    private static readonly string[] StoreOpenIddictEfPackages =
    [
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.EntityFrameworkCore.InMemory",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "OpenIddict.EntityFrameworkCore"
    ];

    [Fact]
    public void Identity_core_and_implementation_projects_are_free_of_concrete_persistence_dependencies()
    {
        var identityRoot = Path.Combine(RepoRoot, "essentials", "src", "Foundation", "Identity");
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
            "essentials",
            "src",
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
    /// Elsa's reusable OpenIddict feature package ships no concrete persistence implementation. The only EF
    /// implementation is the vendor-owned model in the sibling store package, which a host composes explicitly.
    /// </summary>
    public void Elsa_OpenIddict_package_has_no_EF_packages_or_wrapper_sources()
    {
        var openIddictRoot = Path.Combine(RepoRoot, "essentials", "src", "Foundation", "Identity", "OpenIddict");
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

        // No EF wrapper inside the package folder. The one EF store is the sibling package the next test pins.
        Assert.False(Directory.Exists(Path.Combine(packageRoot, "EntityFrameworkCore")));
    }

    [Fact]
    public void The_store_package_owns_the_vendor_package_and_hosts_reference_it()
    {
        var project = XDocument.Load(Path.Combine(
            RepoRoot,
            "essentials", "src", "Foundation", "Identity", "OpenIddict", "EntityFrameworkCore",
            "Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore.csproj"));
        var vendorPackages = project.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include?.Contains("EntityFrameworkCore", StringComparison.Ordinal) == true)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(StoreOpenIddictEfPackages, vendorPackages);

        // Workbench takes the store from the package, never from a vendor package of its own.
        var workbench = XDocument.Load(Path.Combine(RepoRoot, "apps", "Elsa.Workbench", "Elsa.Workbench.csproj"));
        Assert.DoesNotContain(
            workbench.Descendants("PackageReference"),
            element => string.Equals((string?)element.Attribute("Include"), "OpenIddict.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.Contains(
            workbench.Descendants("ProjectReference"),
            element => ((string?)element.Attribute("Include"))?.EndsWith("Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore.csproj", StringComparison.Ordinal) == true);

        var testProject = XDocument.Load(Path.Combine(
            RepoRoot,
            "essentials",
            "tests",
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
