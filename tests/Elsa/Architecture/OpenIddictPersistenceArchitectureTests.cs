using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Guards the durable split: Elsa's OpenIddict package is provider-neutral while Workbench owns its explicit
/// third-party vendor persistence choice.
/// </summary>
public sealed class OpenIddictPersistenceArchitectureTests
{
    private const string WorkbenchVendorRegistrationSha256 = "bdf6f2979f6ce37a39e71ac42b787e6f22bf2be00d07aef35664e3166acab275";

    private static readonly string[] WorkbenchOpenIddictEfPackages =
    [
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.EntityFrameworkCore.InMemory",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "OpenIddict.EntityFrameworkCore"
    ];

    private static readonly IReadOnlyDictionary<string, string> WorkbenchOpenIddictVendorSources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OpenIddictEntityFrameworkCoreDefaults.cs"] = "4e844102195aa3eab13220c513a423345b7100e53365a768e48dfa9f6469d3f2",
            ["OpenIddictIdentityDbContext.cs"] = "105b7bfb61bb87332c8bd5a5a83cff5f8f3f2ee0a40fc6733834afc84b11986f",
            ["OpenIddictIdentityStoreInitializer.cs"] = "e0e4fd06238ee0d890ce278823be670d11cd7e4ce1952a05567895c0403f3a6b",
            ["Sqlite/Migrations/20260704221407_Initial.Designer.cs"] = "e49cc98bb32378c17bbad75fd3bbb071f3d70e7dbf654cc00019282d38e67e79",
            ["Sqlite/Migrations/20260704221407_Initial.cs"] = "d73cc67a51181faa7b1d454fd45bb897f458ecb46156e45dba7aa8cc15229b28",
            ["Sqlite/Migrations/OpenIddictIdentityDbContextModelSnapshot.cs"] = "88338ae62df8596eab3f87d007b121252f373c8670ac1d131d098692b48e27b6",
            ["Sqlite/OpenIddictIdentityDbContextFactory.cs"] = "e6ecc4bbf730d140886b207a3591ae1f027c2930c79938fdd06d0b236803d3ee",
            ["WorkbenchOpenIddictEntityFrameworkCoreOptions.cs"] = "d2442a8e30c18f022cb91806a74a3477f3e6bf136178454d1ac8ca5fbf89d4a1"
        };

    internal static bool IsWorkbenchVendorEfSource(string relativePath)
    {
        const string vendorRoot = "src/Apps/Elsa.Workbench/OpenIddict/";
        if (relativePath == "src/Apps/Elsa.Workbench/WorkbenchOpenIddictVendorRegistration.cs")
            return true;

        return relativePath.StartsWith(vendorRoot, StringComparison.Ordinal) &&
               WorkbenchOpenIddictVendorSources.ContainsKey(relativePath[vendorRoot.Length..]);
    }

    [Fact]
    public void Workbench_vendor_EF_source_allowlist_is_exact()
    {
        const string vendorRoot = "src/Apps/Elsa.Workbench/OpenIddict/";

        Assert.True(IsWorkbenchVendorEfSource("src/Apps/Elsa.Workbench/WorkbenchOpenIddictVendorRegistration.cs"));
        Assert.All(WorkbenchOpenIddictVendorSources.Keys, source => Assert.True(IsWorkbenchVendorEfSource(vendorRoot + source)));
        Assert.False(IsWorkbenchVendorEfSource("src/Apps/Elsa.Workbench/Program.cs"));
        Assert.False(IsWorkbenchVendorEfSource(vendorRoot + "UnlistedEntityFrameworkCoreAdapter.cs"));
        Assert.False(IsWorkbenchVendorEfSource("src/Elsa/Foundation/Identity/OpenIddict/OpenIddictIdentityDbContext.cs"));
    }

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
        var normalizedRegistrationHash = ContentSha256(hostRegistration);
        Assert.True(
            normalizedRegistrationHash == WorkbenchVendorRegistrationSha256,
            "The admitted Workbench OpenIddict vendor registration changed. Re-review its complete content and update the fingerprint deliberately; no additional first-party EF registration may share this exception.");
        Assert.Contains("CShells:Shells:default:Features:FoundationIdentityOpenIddict", hostRegistration, StringComparison.Ordinal);
        Assert.Contains("AddDbContext<OpenIddictIdentityDbContext>", hostRegistration, StringComparison.Ordinal);
        Assert.Contains("UseEntityFrameworkCore", hostRegistration, StringComparison.Ordinal);
        Assert.Contains("AddHostedService", hostRegistration, StringComparison.Ordinal);
        var dbContextIdentifiers = Regex.Matches(hostRegistration, @"\b[A-Za-z_][A-Za-z0-9_]*DbContext\b")
            .Select(match => match.Value)
            .Where(identifier => identifier is not "AddDbContext" and not "UseDbContext")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["OpenIddictIdentityDbContext"], dbContextIdentifiers);
        var dbContextRegistrations = Regex.Matches(hostRegistration, @"\b(?:AddDbContext|UseDbContext)<([^>]+)>")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(["OpenIddictIdentityDbContext", "OpenIddictIdentityDbContext"], dbContextRegistrations);
        var vendorRoot = Path.Combine(RepoRoot, "src", "Apps", "Elsa.Workbench", "OpenIddict");
        Assert.True(Directory.Exists(vendorRoot));
        Assert.All(
            WorkbenchOpenIddictVendorSources,
            source => Assert.True(
                ContentSha256(File.ReadAllText(Path.Join(vendorRoot, source.Key))) == source.Value,
                $"The admitted Workbench OpenIddict vendor source '{source.Key}' changed. Re-review its complete content and update the fingerprint deliberately; no first-party EF code may share this exception."));
        var vendorSources = Directory.EnumerateFiles(vendorRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(vendorRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(WorkbenchOpenIddictVendorSources.Keys, vendorSources);

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

    private static string ContentSha256(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n")))).ToLowerInvariant();

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
