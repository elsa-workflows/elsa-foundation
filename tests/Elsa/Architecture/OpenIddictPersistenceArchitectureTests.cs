using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Guards the temporary split: provider-neutral OpenIddict behavior is reusable by
/// Groundwork while Workbench owns the vendor EF wiring and the legacy project remains the frozen EF oracle until
/// #1471 removes it.
/// </summary>
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
    /// <summary>
    /// The frozen Elsa OpenIddict project still carries the vendor packages/context/migrations as the transitional
    /// oracle. The host-owned registration is intentionally outside this project and will be the only remaining
    /// vendor choice after #1471 removes the frozen slice.
    /// </summary>
    public void Vendor_EF_persistence_keeps_the_pinned_packages_and_excludes_nested_sources()
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
            element => string.Equals((string?)element.Attribute("Remove"), "Behavior/**/*.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void OpenIddict_behavior_composite_does_not_select_vendor_persistence()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "Elsa",
            "Foundation",
            "Identity",
            "OpenIddict",
            "Extensions",
            "OpenIddictIdentityServiceCollectionExtensions.cs"));

        Assert.DoesNotContain("services.AddDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseEntityFrameworkCore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseInMemoryDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseSqlite", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenIddictIdentityStoreInitializer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Workbench_owns_the_vendor_package_and_explicit_registration()
    {
        var project = XDocument.Load(Path.Combine(
            RepoRoot,
            "src",
            "Apps",
            "Elsa.Workbench",
            "Elsa.Workbench.csproj"));
        var program = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "Apps",
            "Elsa.Workbench",
            "Program.cs"));

        Assert.Contains(
            project.Descendants("PackageReference"),
            element => string.Equals((string?)element.Attribute("Include"), "OpenIddict.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.Contains("AddWorkbenchOpenIddictVendor", program, StringComparison.Ordinal);

        var hostRegistration = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "Apps",
            "Elsa.Workbench",
            "WorkbenchOpenIddictVendorRegistration.cs"));
        Assert.Contains("CShells:Shells:default:Features:FoundationIdentityOpenIddict", hostRegistration, StringComparison.Ordinal);
        Assert.Contains("AddDbContext<OpenIddictIdentityDbContext>", hostRegistration, StringComparison.Ordinal);
        Assert.Contains("UseEntityFrameworkCore", hostRegistration, StringComparison.Ordinal);
        Assert.Contains("AddHostedService", hostRegistration, StringComparison.Ordinal);
    }

    private static bool IsSourceOrProject(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ForbiddenLines(string path, params string[] tokens)
    {
        var relativePath = Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        return File.ReadLines(path)
            .Select((line, index) => (line, number: index + 1))
            .Where(candidate => tokens.Any(token => candidate.line.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Select(candidate => $"{relativePath}:{candidate.number}: {candidate.line.Trim()}");
    }

    private static IReadOnlyList<string> FindEfDependencyPaths(string rootProjectPath)
    {
        var violations = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit(rootProjectPath, []);
        return violations;

        void Visit(string projectPath, IReadOnlyList<string> incomingPath)
        {
            var fullPath = Path.GetFullPath(projectPath);
            var relativePath = Path.GetRelativePath(RepoRoot, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var dependencyPath = incomingPath.Append(relativePath).ToArray();

            if (!visited.Add(fullPath))
                return;

            if (!File.Exists(fullPath))
            {
                violations.Add($"{string.Join(" -> ", dependencyPath)} (missing project)");
                return;
            }

            var project = XDocument.Load(fullPath);
            var efPackages = project.Descendants("PackageReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(include => include?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) == true);

            violations.AddRange(efPackages.Select(package =>
                $"{string.Join(" -> ", dependencyPath)} -> package:{package}"));

            foreach (var reference in project.Descendants("ProjectReference"))
            {
                var include = (string?)reference.Attribute("Include");
                if (string.IsNullOrWhiteSpace(include))
                {
                    violations.Add($"{string.Join(" -> ", dependencyPath)} -> unresolved empty ProjectReference");
                    continue;
                }

                if (include.Contains("$(", StringComparison.Ordinal))
                {
                    violations.Add($"{string.Join(" -> ", dependencyPath)} -> unresolved ProjectReference:{include}");
                    continue;
                }

                var normalizedInclude = include
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var referencedProject = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(fullPath)!, normalizedInclude));
                Visit(referencedProject, dependencyPath);
            }
        }
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
