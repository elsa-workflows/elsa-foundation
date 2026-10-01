using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    private const string WorkbenchVendorRegistrationSha256 = "8dc4be11633a927ffa72b13d9ca30dab4438579bd98466dd111facb931d7b65c";

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

    private static readonly IReadOnlyDictionary<string, string> WorkbenchOpenIddictVendorSources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OpenIddictEntityFrameworkCoreDefaults.cs"] = "4e844102195aa3eab13220c513a423345b7100e53365a768e48dfa9f6469d3f2",
            ["OpenIddictIdentityDbContext.cs"] = "dbc4b8677f673a3bf4c09ce4ce2541e21e7cb6149c8a46460fe03b8e2f5e4955",
            ["OpenIddictIdentityProviderDbContextFactories.cs"] = "d4c1ebc49956d4e7a242f97f42c09a5ddfcea34d9f8d8c95422d16a5452ef018",
            ["OpenIddictIdentityProviderDbContexts.cs"] = "8cc05b66ed024dc6bcf917b8c7faf571525753c7d9617385bdbe450a1ada343d",
            ["OpenIddictIdentityStoreInitializer.cs"] = "e0e4fd06238ee0d890ce278823be670d11cd7e4ce1952a05567895c0403f3a6b",
            ["PostgreSql/Migrations/20261001054059_Initial.Designer.cs"] = "6b93634c66b2da424b88a168b34dd07b167e3b66ae835046a37ef21ae787735a",
            ["PostgreSql/Migrations/20261001054059_Initial.cs"] = "aa9c7dc31aa45c69ff57ea636831f8d3e63f6e4ade5bf85e8cf65738a9e4cd16",
            ["PostgreSql/Migrations/OpenIddictIdentityPostgreSqlDbContextModelSnapshot.cs"] = "516ca40f36331dfb6f494784d4fa00dfbc9fedf323722aa6b941b2902f2b7184",
            ["SqlServer/Migrations/20261001054055_Initial.Designer.cs"] = "5064aa0bfedb49c2d97881cb7b60f9c8725b3eacc617059e9d4a449519e4aca4",
            ["SqlServer/Migrations/20261001054055_Initial.cs"] = "f348f474a770615627059404707eaea256ea609eb473fa9fc5c7fca859783f42",
            ["SqlServer/Migrations/OpenIddictIdentitySqlServerDbContextModelSnapshot.cs"] = "c19b0a6fbeeda3773ceb2b33b3a3e914b11d5265b69ccca4608cea781454b617",
            ["Sqlite/Migrations/20260704221407_Initial.Designer.cs"] = "e49cc98bb32378c17bbad75fd3bbb071f3d70e7dbf654cc00019282d38e67e79",
            ["Sqlite/Migrations/20260704221407_Initial.cs"] = "d73cc67a51181faa7b1d454fd45bb897f458ecb46156e45dba7aa8cc15229b28",
            ["Sqlite/Migrations/OpenIddictIdentityDbContextModelSnapshot.cs"] = "88338ae62df8596eab3f87d007b121252f373c8670ac1d131d098692b48e27b6",
            ["Sqlite/OpenIddictIdentityDbContextFactory.cs"] = "ee2d3de5e2a2b4c9909bd3f8d075f7f65dab9ee1263ecd5d7e1ce25709b50736",
            ["WorkbenchOpenIddictEntityFrameworkCoreOptions.cs"] = "d2442a8e30c18f022cb91806a74a3477f3e6bf136178454d1ac8ca5fbf89d4a1"
        };

    internal static bool IsWorkbenchVendorEfSource(string relativePath)
    {
        const string vendorRoot = "src/apps/Elsa.Workbench/OpenIddict/";
        if (relativePath == "src/apps/Elsa.Workbench/WorkbenchOpenIddictVendorRegistration.cs")
            return true;

        return relativePath.StartsWith(vendorRoot, StringComparison.Ordinal) &&
               WorkbenchOpenIddictVendorSources.ContainsKey(relativePath[vendorRoot.Length..]);
    }

    [Fact]
    public void Workbench_vendor_EF_source_allowlist_is_exact()
    {
        const string vendorRoot = "src/apps/Elsa.Workbench/OpenIddict/";

        Assert.True(IsWorkbenchVendorEfSource("src/apps/Elsa.Workbench/WorkbenchOpenIddictVendorRegistration.cs"));
        Assert.All(WorkbenchOpenIddictVendorSources.Keys, source => Assert.True(IsWorkbenchVendorEfSource(vendorRoot + source)));
        Assert.False(IsWorkbenchVendorEfSource("src/apps/Elsa.Workbench/Program.cs"));
        Assert.False(IsWorkbenchVendorEfSource(vendorRoot + "UnlistedEntityFrameworkCoreAdapter.cs"));
        Assert.False(IsWorkbenchVendorEfSource("src/essentials/Foundation/Identity/OpenIddict/OpenIddictIdentityDbContext.cs"));
    }

    [Fact]
    public void Identity_core_and_implementation_projects_are_free_of_concrete_persistence_dependencies()
    {
        var identityRoot = Path.Combine(RepoRoot, "src", "essentials", "Foundation", "Identity");
        var coreFiles = Directory.EnumerateFiles(Path.Combine(identityRoot, "Core"), "*", SearchOption.AllDirectories);
        var implementationFiles = OwnProjectFiles(identityRoot).ToArray();

        // The implementation project sits at the domain root above its sibling sub-projects. Prove the
        // ownership filter still reaches its own project file and sources, so it cannot pass by scanning nothing.
        Assert.Contains(Path.Combine(identityRoot, "Elsa.Foundation.Identity.csproj"), implementationFiles);
        Assert.Contains(Path.Combine(identityRoot, "Extensions", "FoundationIdentityServiceCollectionExtensions.cs"), implementationFiles);

        var violations = coreFiles
            .Concat(implementationFiles)
            .Where(IsSourceOrProject)
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
            .Where(IsSourceOrProject)
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
        var projectPath = Path.Combine(
            RepoRoot,
            "src",
            "essentials",
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
            "essentials",
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
            "essentials",
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

    /// <summary>
    /// Elsa's engine selection, migration policy and prune sit beside the vendor store in Workbench files of their own, which name
    /// no EF package namespace, so the vendor exception holds nothing of Elsa's. The host's entry point composes them in the order
    /// they depend on: the engine over the vendor's context, the migration before the prune (#2201).
    /// </summary>
    [Fact]
    public void Workbench_composes_the_elsa_openiddict_policies_beside_the_vendor_registration_in_order()
    {
        var workbench = Path.Combine(RepoRoot, "src", "apps", "Elsa.Workbench");
        var program = File.ReadAllText(Path.Combine(workbench, "Program.cs"));
        var calls = new[]
        {
            "AddWorkbenchOpenIddictVendor(",
            "AddWorkbenchOpenIddictStoreProvider(",
            "AddWorkbenchOpenIddictMigrationPolicy(",
            "AddWorkbenchOpenIddictPruning("
        };

        var positions = calls.Select(call => program.IndexOf(call, StringComparison.Ordinal)).ToArray();

        Assert.All(positions, position => Assert.True(position >= 0, "Program.cs no longer composes one of the Workbench OpenIddict registrations."));
        Assert.Equal(positions.Order(), positions);
        foreach (var policy in new[] { "WorkbenchOpenIddictStoreProvider.cs", "WorkbenchOpenIddictMigrationPolicy.cs", "WorkbenchOpenIddictPruning.cs" })
        {
            Assert.False(IsWorkbenchVendorEfSource($"src/apps/Elsa.Workbench/{policy}"), $"{policy} is Elsa's policy, and must not share the vendor exception.");
            Assert.DoesNotContain("EntityFrameworkCore", File.ReadAllText(Path.Combine(workbench, policy)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Workbench_owns_the_vendor_package_and_explicit_registration()
    {
        var project = XDocument.Load(Path.Combine(
            RepoRoot,
            "src",
            "apps",
            "Elsa.Workbench",
            "Elsa.Workbench.csproj"));
        var program = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "apps",
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
            "apps",
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
        var vendorRoot = Path.Combine(RepoRoot, "src", "apps", "Elsa.Workbench", "OpenIddict");
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

    /// <summary>
    /// Files owned by the project rooted at <paramref name="projectDirectory"/>: every file whose nearest
    /// enclosing directory with a project file is that root. The walk stops at any directory holding its own
    /// project, so a nested sub-project is excluded because it is a project, not because it is named in a
    /// list that could drift from the tree.
    /// </summary>
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
}
