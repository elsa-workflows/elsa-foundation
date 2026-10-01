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
    private const string WorkbenchVendorRegistrationSha256 = "46457f7810f5b58ac5237aeb5b6a7683800fce9b3ed6459767e3c3ff9f3787ed";

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
            ["OpenIddictIdentityStoreInitializer.cs"] = "e0e4fd06238ee0d890ce278823be670d11cd7e4ce1952a05567895c0403f3a6b",
            ["Sqlite/Migrations/20260704221407_Initial.Designer.cs"] = "e49cc98bb32378c17bbad75fd3bbb071f3d70e7dbf654cc00019282d38e67e79",
            ["Sqlite/Migrations/20260704221407_Initial.cs"] = "d73cc67a51181faa7b1d454fd45bb897f458ecb46156e45dba7aa8cc15229b28",
            ["Sqlite/Migrations/OpenIddictIdentityDbContextModelSnapshot.cs"] = "88338ae62df8596eab3f87d007b121252f373c8670ac1d131d098692b48e27b6",
            ["Sqlite/OpenIddictIdentityDbContextFactory.cs"] = "ee2d3de5e2a2b4c9909bd3f8d075f7f65dab9ee1263ecd5d7e1ce25709b50736",
            ["WorkbenchOpenIddictEntityFrameworkCoreOptions.cs"] = "d2442a8e30c18f022cb91806a74a3477f3e6bf136178454d1ac8ca5fbf89d4a1"
        };

    /// <summary>
    /// Elsa's own per-engine contexts, design-time factories and scaffolded migrations for the vendor model (#2201), an exception of
    /// their own and not part of the vendor's. They are Elsa-authored, not third-party, but they are EF code that names the EF
    /// namespaces and so cannot sit in a file that must not; the vendor model is the only model they carry, and they hold no logic
    /// beside it: each context is an empty subclass of the vendor context, which EF Core needs to tell one engine's migrations
    /// from another's, and every migration is scaffolded, not written. The engine's selection, binding and connection logic, which
    /// is Elsa's policy, stays in Workbench files that name no EF namespace. Fingerprinted, so that nothing else joins them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> WorkbenchOpenIddictEngineSources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OpenIddictIdentityProviderDbContextFactories.cs"] = "bab20a001ee89beb24d255488a74374c7bccd646d8cfe4603eb6bf5eb49036c6",
            ["OpenIddictIdentityProviderDbContexts.cs"] = "9db2f5d373233a2e0b3e8d3b407b56d8dd6a645a69899211cc98fec477734912",
            ["PostgreSql/Migrations/20261001054059_Initial.Designer.cs"] = "28d6549aaca6b2e234ed371d2b8debab24bb586dcb79cf34bd22553b0671218f",
            ["PostgreSql/Migrations/20261001054059_Initial.cs"] = "4f5b6b5da341b1ee0c00a39b8b80e8b325d424f8eb019f3c1a642edfb467f05a",
            ["PostgreSql/Migrations/OpenIddictIdentityPostgreSqlDbContextModelSnapshot.cs"] = "00df95c8ef3d63c2b68546ffb2a3995e605cf4d8cf67f1c8496b248776577e76",
            ["SqlServer/Migrations/20261001054055_Initial.Designer.cs"] = "336bb28a80a5735ad61fd8492e07681fa8d1a5b1636793a6b82161eaa7974d58",
            ["SqlServer/Migrations/20261001054055_Initial.cs"] = "7ac8769250ff9f17638553a0359a6b2ab64abc5c9d127e529b2a0faec64b6d97",
            ["SqlServer/Migrations/OpenIddictIdentitySqlServerDbContextModelSnapshot.cs"] = "0815e26f6657152d44eb4c3bb57aeae8c12105503b31db370bb444fc70d381ff"
        };

    internal static bool IsWorkbenchEngineEfSource(string relativePath)
    {
        const string engineRoot = "src/apps/Elsa.Workbench/OpenIddictEngines/";
        return relativePath.StartsWith(engineRoot, StringComparison.Ordinal) &&
               WorkbenchOpenIddictEngineSources.ContainsKey(relativePath[engineRoot.Length..]);
    }

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
    /// Elsa's per-engine contexts, factories and migrations are an exception of their own, listed and fingerprinted apart from the
    /// vendor's: the vendor directory holds vendor files and nothing else, and the engine directory holds exactly the listed files,
    /// each context an empty subclass of the vendor context (#2201).
    /// </summary>
    [Fact]
    public void Workbench_engine_EF_source_exception_is_its_own_and_exact()
    {
        const string engineRoot = "src/apps/Elsa.Workbench/OpenIddictEngines/";
        const string vendorRoot = "src/apps/Elsa.Workbench/OpenIddict/";
        var engineDirectory = Path.Join(RepoRoot, "src", "apps", "Elsa.Workbench", "OpenIddictEngines");

        Assert.All(WorkbenchOpenIddictEngineSources.Keys, source =>
        {
            Assert.True(IsWorkbenchEngineEfSource(engineRoot + source));
            Assert.False(IsWorkbenchVendorEfSource(engineRoot + source), $"{source} is Elsa's engine code and must not share the vendor exception.");
            Assert.False(IsWorkbenchEngineEfSource(vendorRoot + source));
        });
        Assert.All(WorkbenchOpenIddictVendorSources.Keys, source => Assert.False(IsWorkbenchEngineEfSource(engineRoot + source)));
        Assert.False(IsWorkbenchEngineEfSource(engineRoot + "UnlistedEntityFrameworkCoreAdapter.cs"));
        Assert.False(IsWorkbenchEngineEfSource("src/apps/Elsa.Workbench/Program.cs"));

        Assert.True(Directory.Exists(engineDirectory));
        Assert.All(
            WorkbenchOpenIddictEngineSources,
            source => Assert.True(
                ContentSha256(File.ReadAllText(Path.Join(engineDirectory, source.Key))) == source.Value,
                $"The admitted Workbench OpenIddict engine source '{source.Key}' changed. Re-review its complete content and update the fingerprint deliberately; nothing but the vendor model's per-engine contexts, factories and scaffolded migrations may share this exception."));
        var engineSources = Directory.EnumerateFiles(engineDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(engineDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(WorkbenchOpenIddictEngineSources.Keys, engineSources);

        // The contexts carry the vendor model and nothing of Elsa's: no members of their own.
        var contexts = File.ReadAllText(Path.Join(engineDirectory, "OpenIddictIdentityProviderDbContexts.cs"));
        Assert.DoesNotContain("override", contexts, StringComparison.Ordinal);
        Assert.DoesNotContain("{", contexts, StringComparison.Ordinal);
    }

    /// <summary>
    /// Elsa's engine selection, migration policy and prune sit beside the vendor store in Workbench files of their own, which name
    /// no EF package namespace and are in neither exception, so no logic of Elsa's hides in one (#2201).
    /// </summary>
    [Fact]
    public void Workbench_elsa_openiddict_policies_are_outside_both_EF_exceptions()
    {
        var workbench = Path.Join(RepoRoot, "src", "apps", "Elsa.Workbench");
        foreach (var policy in new[] { "WorkbenchOpenIddictStoreProvider.cs", "WorkbenchOpenIddictMigrationPolicy.cs", "WorkbenchOpenIddictPruning.cs" })
        {
            var relativePath = $"src/apps/Elsa.Workbench/{policy}";
            Assert.False(IsWorkbenchVendorEfSource(relativePath), $"{policy} is Elsa's policy, and must not share the vendor exception.");
            Assert.False(IsWorkbenchEngineEfSource(relativePath), $"{policy} is Elsa's policy, and must not share the engine exception.");
            Assert.DoesNotContain("EntityFrameworkCore", File.ReadAllText(Path.Join(workbench, policy)), StringComparison.Ordinal);
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
