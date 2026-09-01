using System.Xml.Linq;
using Xunit;

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
        "Microsoft.EntityFrameworkCore.Sqlite",
        "OpenIddict.EntityFrameworkCore"
    ];

    private static readonly string[] WorkbenchOpenIddictVendorSources =
    [
        "OpenIddictEntityFrameworkCoreDefaults.cs",
        "OpenIddictIdentityDbContext.cs",
        "OpenIddictIdentityStoreInitializer.cs",
        "Sqlite/Migrations/20260704221407_Initial.Designer.cs",
        "Sqlite/Migrations/20260704221407_Initial.cs",
        "Sqlite/Migrations/OpenIddictIdentityDbContextModelSnapshot.cs",
        "Sqlite/OpenIddictIdentityDbContextFactory.cs",
        "WorkbenchOpenIddictEntityFrameworkCoreOptions.cs"
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
    /// Elsa's reusable OpenIddict package ships no concrete persistence implementation. The only retained EF
    /// implementation is the vendor-owned model selected explicitly by Workbench.
    /// </summary>
    public void Elsa_OpenIddict_package_has_no_EF_packages_or_wrapper_sources()
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

        Assert.Empty(efPackages);
        Assert.Contains(
            project.Descendants("Compile"),
            element => string.Equals((string?)element.Attribute("Remove"), "Behavior/**/*.cs", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(
            RepoRoot,
            "src",
            "Elsa",
            "Foundation",
            "Identity",
            "OpenIddict",
            "EntityFrameworkCore")));
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
        var vendorPackages = project.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include?.Contains("EntityFrameworkCore", StringComparison.Ordinal) == true)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(WorkbenchOpenIddictEfPackages, vendorPackages);
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
        var vendorRoot = Path.Combine(RepoRoot, "src", "Apps", "Elsa.Workbench", "OpenIddict");
        Assert.True(Directory.Exists(vendorRoot));
        var vendorSources = Directory.EnumerateFiles(vendorRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(vendorRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(WorkbenchOpenIddictVendorSources, vendorSources);

        var testProject = XDocument.Load(Path.Combine(
            RepoRoot,
            "tests",
            "Elsa",
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
            ["Microsoft.EntityFrameworkCore.InMemory", "OpenIddict.EntityFrameworkCore"],
            testVendorPackages);
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
