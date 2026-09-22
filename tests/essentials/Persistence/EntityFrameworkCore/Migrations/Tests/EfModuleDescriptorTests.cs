using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Xml.Linq;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Guards the single <c>[EfModule]</c> declaration every module assembly now carries (ADR 0076 D2, spec
/// 171 slice 1). <see cref="EfModuleCatalog.Discover"/> must find all 13 first-party modules with the
/// names, base contexts and frozen history names the vocabulary table pins, and a descriptor's
/// <c>HistoryModule</c> must never drift from the module's own frozen
/// <c>&lt;Module&gt;EfModule.HistoryModuleName</c> constant — the exact mismatch already latent between
/// <c>RuntimeEfModule.HistoryModuleName</c> and <see cref="ModuleContextCatalog"/>'s independently
/// re-derived history table before this descriptor existed.
/// </summary>
public sealed class EfModuleDescriptorTests
{
    // ManifestExtensionAttribute (Elsa.Specifications.PackageManifest.Generator.Hints) compiles as internal
    // into each module assembly separately, so it is matched by full type name and read reflectively — the
    // same way ManifestHintReader reads the generator's other hint attributes — rather than referenced.
    private const string ManifestExtensionAttributeFullName = "Elsa.Specifications.PackageManifest.Generator.Hints.ManifestExtensionAttribute";
    private const string EfModulesExtensionKey = "efModules";

    // Read directly from each module's own frozen constant and base context, not re-derived, so this
    // guard catches the exact drift the descriptor exists to prevent.
    private static readonly (string Name, Type Context, string HistoryModule)[] Expected =
    [
        ("Secrets", typeof(SecretsDbContext), SecretsEfModule.HistoryModuleName),
        ("Workflows.Runtime", typeof(RuntimeDbContext), RuntimeEfModule.HistoryModuleName),
        ("Workflows.Design", typeof(WorkflowsDesignDbContext), WorkflowsDesignEfModule.HistoryModuleName),
        ("Workflows.Publishing", typeof(PublishingSnapshotReviewDbContext), PublishingSnapshotReviewEfModule.HistoryModuleName),
        ("Workflows.Runtime.Distributed.Placement", typeof(ExecutionPlacementDbContext), ExecutionPlacementEfModule.HistoryModuleName),
        ("Workflows.Runtime.Distributed.CommandTransport", typeof(ExecutionCommandTransportDbContext), ExecutionCommandTransportEfModule.HistoryModuleName),
        ("Activities.Design", typeof(ActivitiesDesignDbContext), ActivitiesDesignEfModule.HistoryModuleName),
        ("Identity.Iam", typeof(IdentityIamDbContext), IdentityIamEfModule.HistoryModuleName),
        ("Identity.ProviderConfiguration", typeof(IdentityProviderConfigurationDbContext), IdentityProviderConfigurationEfModule.HistoryModuleName),
        ("Diagnostics.OpenTelemetry", typeof(EfOpenTelemetryDbContext), EfOpenTelemetryModule.HistoryModuleName),
        ("Diagnostics.StructuredLogs", typeof(StructuredLogsDbContext), StructuredLogsEfModule.HistoryModuleName),
        ("Studio.Preferences", typeof(StudioPreferencesDbContext), StudioPreferencesEfModule.HistoryModuleName),
        ("Elsa3.Activities.Design.Import", typeof(Elsa3ImportDbContext), Elsa3ImportEfModule.HistoryModuleName)
    ];

    private static IReadOnlyList<EfModuleDescriptor> Discover() => EfModuleCatalog.Discover(ModuleContextCatalog.Modules);

    [Fact]
    public void Discover_returns_all_13_modules_with_correct_names_contexts_and_frozen_history()
    {
        var descriptors = Discover();
        Assert.Equal(13, descriptors.Count);

        foreach (var (name, context, historyModule) in Expected)
        {
            var descriptor = Assert.Single(descriptors, d => d.Name == name);
            Assert.Equal(context, descriptor.ContextType);
            Assert.Equal(historyModule, descriptor.HistoryModule);
            Assert.Equal(EfMigrationsHistory.TableName(historyModule), descriptor.HistoryTableName);
        }
    }

    [Fact]
    public void Every_module_name_is_unique_case_insensitively()
    {
        var collisions = DescribeDuplicates(
            Discover(),
            descriptor => descriptor.Name,
            StringComparer.OrdinalIgnoreCase,
            descriptor => $"{descriptor.ContextType.Name} in {descriptor.Assembly.GetName().Name}");

        Assert.True(collisions.Length == 0, $"Module names declared more than once: {string.Join("; ", collisions)}.");
    }

    [Fact]
    public void Every_history_module_is_unique()
    {
        var collisions = DescribeDuplicates(
            Discover(),
            descriptor => descriptor.HistoryModule,
            StringComparer.Ordinal,
            descriptor => descriptor.Name);

        Assert.True(collisions.Length == 0, $"History modules shared by more than one module: {string.Join("; ", collisions)}.");
    }

    /// <summary>Groups <paramref name="descriptors"/> by <paramref name="key"/>, naming the colliding key and every module that declares it.</summary>
    private static string[] DescribeDuplicates<TKey>(
        IReadOnlyList<EfModuleDescriptor> descriptors,
        Func<EfModuleDescriptor, TKey> key,
        IEqualityComparer<TKey> comparer,
        Func<EfModuleDescriptor, string> describe) =>
        descriptors
            .GroupBy(key, comparer)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} <- {string.Join(", ", group.Select(describe))}")
            .ToArray();

    /// <summary>
    /// Spec 171 slice 11 (#1881, ADR 0076 D2): each module assembly also carries one
    /// <c>[ManifestExtension("efModules", "&lt;Name&gt;")]</c> per <c>[EfModule]</c> declaration, so the pack-time
    /// generator mirrors the descriptor's name(s) into <c>elsa-package.json</c>'s <c>extensions.efModules</c>. The
    /// name is written twice — once on each attribute — so this test, not a third shared constant, is what keeps
    /// them from drifting apart: it fails if a module's mirrored names are missing any declared name, or carry one
    /// no <c>[EfModule]</c> declares.
    /// </summary>
    [Fact]
    public void Every_module_assembly_mirrors_its_EfModule_names_into_a_ManifestExtension_efModules_declaration()
    {
        var descriptorNamesByAssembly = Discover()
            .GroupBy(descriptor => descriptor.Assembly)
            .ToDictionary(group => group.Key, group => group.Select(descriptor => descriptor.Name).ToHashSet(StringComparer.Ordinal));

        foreach (var assembly in ModuleContextCatalog.Modules)
        {
            var declared = descriptorNamesByAssembly.TryGetValue(assembly, out var names) ? names : [];
            var mirrored = ReadManifestExtensionEfModules(assembly);

            var missing = declared.Except(mirrored).ToArray();
            var extra = mirrored.Except(declared).ToArray();
            Assert.True(
                missing.Length == 0 && extra.Length == 0,
                $"{assembly.GetName().Name}: [EfModule] declares {string.Join(", ", declared)}, but " +
                $"[ManifestExtension(\"efModules\", ...)] mirrors {string.Join(", ", mirrored)} " +
                $"(missing: {string.Join(", ", missing)}; extra not declared: {string.Join(", ", extra)}).");
        }
    }

    /// <summary>Reflects <c>[ManifestExtension("efModules", value)]</c> off <paramref name="assembly"/> by full type name (the
    /// attribute is internal and compiles separately into every module assembly), mirroring how <c>ManifestHintReader</c>
    /// reads the generator's other hint attributes without referencing the generator package from this test project.</summary>
    private static HashSet<string> ReadManifestExtensionEfModules(Assembly assembly) =>
        assembly.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName == ManifestExtensionAttributeFullName)
            .Where(attribute => attribute.ConstructorArguments is [{ Value: string key }, { Value: string }] && key == EfModulesExtensionKey)
            .Select(attribute => (string)attribute.ConstructorArguments[1].Value!)
            .ToHashSet(StringComparer.Ordinal);

    // One project directory per module assembly (ADR 0076 D2: eleven module projects carry thirteen
    // [EfModule] declarations; Identity and Workflows.Runtime.Distributed each carry two in one project).
    // Repo-relative, not derived from the assembly's bin/obj output path, for the same reason
    // SecretsEfPersistencePilotArchitectureTests reads project files from the repo rather than from disk
    // layout: it is the source, not the build output, that a guard test must hold to account.
    private static readonly (Assembly Assembly, string ProjectDirectory)[] ModuleProjectDirectories =
    [
        (typeof(ActivitiesDesignDbContext).Assembly, "src/essentials/Activities/Design/Persistence/EntityFrameworkCore"),
        (typeof(EfOpenTelemetryDbContext).Assembly, "src/essentials/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore"),
        (typeof(StructuredLogsDbContext).Assembly, "src/essentials/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore"),
        (typeof(IdentityIamDbContext).Assembly, "src/essentials/Foundation/Identity/Persistence/EntityFrameworkCore"),
        (typeof(SecretsDbContext).Assembly, "src/essentials/Secrets/Persistence/EntityFrameworkCore"),
        (typeof(StudioPreferencesDbContext).Assembly, "src/essentials/Studio/Preferences/Persistence/EntityFrameworkCore"),
        (typeof(WorkflowsDesignDbContext).Assembly, "src/essentials/Workflows/Design/Persistence/EntityFrameworkCore"),
        (typeof(PublishingSnapshotReviewDbContext).Assembly, "src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore"),
        (typeof(ExecutionPlacementDbContext).Assembly, "src/essentials/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore"),
        (typeof(RuntimeDbContext).Assembly, "src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore"),
        (typeof(Elsa3ImportDbContext).Assembly, "src/extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore")
    ];

    /// <summary>
    /// Spec 172 D1, D2 (#1938, child of #1936): every EF module package declares capability <c>ef-provider</c> in a
    /// package-root <c>nuplane.json</c> (schema 2), one option per non-null <c>[EfModule]</c> provider property, so
    /// a package-hosted host can select its engine instead of naming it by hand in the closure. This guard reads
    /// the truth from the three places it already lives — <c>[EfModule]</c> (via <see cref="Discover"/>),
    /// <see cref="EfRelationalProviderBinding.ProviderPackageId"/>, and the central pin in
    /// <c>Directory.Packages.props</c> — so a provider added or dropped, an engine id renamed, or a pin bumped
    /// without the declaring file following, fails here.
    /// </summary>
    [Fact]
    public void Every_module_package_declares_an_ef_provider_capability_matching_its_EfModule_and_the_pinned_engines()
    {
        var descriptorsByAssembly = Discover().GroupBy(descriptor => descriptor.Assembly).ToDictionary(group => group.Key, group => group.ToArray());
        var projectDirectoriesByAssembly = ModuleProjectDirectories.ToDictionary(entry => entry.Assembly, entry => entry.ProjectDirectory);
        var catalogAssemblies = ModuleContextCatalog.Modules.ToHashSet();

        // Both directions, so ModuleProjectDirectories can only shrink or grow together with
        // ModuleContextCatalog.Modules: a twelfth module added to the catalog without a table entry fails
        // here by name, and so does a stale table entry for an assembly the catalog no longer carries.
        foreach (var (assembly, _) in ModuleProjectDirectories)
            Assert.True(
                catalogAssemblies.Contains(assembly),
                $"{assembly.GetName().Name} has a {nameof(ModuleProjectDirectories)} entry but is not in {nameof(ModuleContextCatalog)}.{nameof(ModuleContextCatalog.Modules)}.");

        foreach (var assembly in ModuleContextCatalog.Modules)
        {
            Assert.True(
                projectDirectoriesByAssembly.TryGetValue(assembly, out var projectDirectory),
                $"{assembly.GetName().Name} is in {nameof(ModuleContextCatalog)}.{nameof(ModuleContextCatalog.Modules)} but has no entry in {nameof(ModuleProjectDirectories)}.");

            var descriptors = descriptorsByAssembly[assembly];
            var expectedOptionNames = ModuleContextCatalog.Providers
                .Where(provider => descriptors.Any(descriptor => descriptor.ProviderContext(provider) is not null))
                .ToArray();

            var directory = RepoPath(projectDirectory!.Split('/'));
            var metadataPath = Path.Join(directory, "nuplane.json");
            Assert.True(File.Exists(metadataPath), $"{projectDirectory} has no nuplane.json.");

            using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
            Assert.Equal(2, metadata.RootElement.GetProperty("schemaVersion").GetInt32());

            var capability = Assert.Single(metadata.RootElement.GetProperty("capabilities").EnumerateArray());
            Assert.Equal("ef-provider", capability.GetProperty("name").GetString());

            var options = capability.GetProperty("options").EnumerateArray()
                .ToDictionary(option => option.GetProperty("name").GetString()!, option => option, StringComparer.Ordinal);

            Assert.Equal(
                expectedOptionNames.OrderBy(name => name, StringComparer.Ordinal),
                options.Keys.OrderBy(name => name, StringComparer.Ordinal));

            foreach (var provider in expectedOptionNames)
            {
                var packageId = EfRelationalProviderBinding.ProviderPackageId(provider);
                var option = options[provider];
                Assert.Equal(packageId, option.GetProperty("packageId").GetString());
                Assert.Equal($"[{PinnedVersion(packageId)}]", option.GetProperty("version").GetString());
            }

            var csprojPath = Assert.Single(Directory.EnumerateFiles(directory, "*.csproj"));
            var project = XDocument.Load(csprojPath);
            var metadataItem = Assert.Single(project.Descendants("None"), element =>
                string.Equals(element.Attribute("Update")?.Value, "nuplane.json", StringComparison.Ordinal));
            Assert.Equal("true", metadataItem.Attribute("Pack")?.Value);
            Assert.Equal("/", metadataItem.Attribute("PackagePath")?.Value);
        }
    }

    /// <summary>The central pin for <paramref name="packageId"/> in <c>Directory.Packages.props</c>, not hardcoded here so an engine bump fails this test instead of silently drifting from it.</summary>
    private static string PinnedVersion(string packageId) => PackageVersionPins.Value[packageId];

    private static readonly Lazy<IReadOnlyDictionary<string, string>> PackageVersionPins = new(() =>
        XDocument.Load(RepoPath("Directory.Packages.props"))
            .Descendants("PackageVersion")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal));

    private static string RepoPath(params string[] segments) => Path.Join([RepoRoot, ..segments]);

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }
    }

    [Fact]
    public void Secrets_declares_all_four_providers()
    {
        var secrets = Discover().Single(descriptor => descriptor.Name == "Secrets");
        Assert.NotNull(secrets.Sqlite);
        Assert.NotNull(secrets.SqlServer);
        Assert.NotNull(secrets.PostgreSql);
        Assert.NotNull(secrets.MySql);
    }

    [Fact]
    public void Every_declared_provider_context_derives_from_the_base_context_and_follows_the_provider_suffix()
    {
        foreach (var descriptor in Discover())
        {
            foreach (var provider in ModuleContextCatalog.Providers)
            {
                var context = descriptor.RequireProviderContext(provider);
                Assert.True(
                    descriptor.ContextType.IsAssignableFrom(context),
                    $"{descriptor.Name}: {context.Name} does not derive from {descriptor.ContextType.Name}.");
                Assert.EndsWith(provider + "DbContext", context.Name, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Every_provider_derived_context_is_covered_by_exactly_one_module()
    {
        var declared = Discover()
            .SelectMany(descriptor => ModuleContextCatalog.Providers.Select(descriptor.RequireProviderContext))
            .ToArray();

        Assert.Equal(declared.Length, declared.Distinct().Count());
        Assert.Equal(
            ModuleContextCatalog.AllContexts().OrderBy(type => type.FullName, StringComparer.Ordinal),
            declared.OrderBy(type => type.FullName, StringComparer.Ordinal));
    }

    /// <summary>
    /// Neither project owns a context or migrations of its own — Dashboard reads Design and Runtime's
    /// contexts read-only, and AspNetCoreIdentity reads Identity.Iam's — so neither declares a module.
    /// </summary>
    [Fact]
    public void The_two_context_free_ef_projects_declare_no_module()
    {
        var dashboard = typeof(WorkflowsDashboardEntityFrameworkCoreFeature).Assembly;
        var aspNetCoreIdentity = typeof(AspNetCoreIdentityEntityFrameworkCoreFeature).Assembly;

        var descriptors = EfModuleCatalog.Discover(ModuleContextCatalog.Modules.Append(dashboard).Append(aspNetCoreIdentity).Distinct());

        Assert.Equal(13, descriptors.Count);
        Assert.DoesNotContain(descriptors, descriptor => descriptor.Assembly == dashboard);
        Assert.DoesNotContain(descriptors, descriptor => descriptor.Assembly == aspNetCoreIdentity);
    }

    [Fact]
    public void ProviderContext_returns_null_for_an_unsupported_provider_and_RequireProviderContext_throws_a_clear_error()
    {
        var descriptor = new EfModuleDescriptor(
            "Acme.Widgets",
            typeof(object),
            "ElsaAcmeWidgets",
            Sqlite: typeof(object),
            SqlServer: null,
            PostgreSql: typeof(object),
            MySql: typeof(object),
            DependsOn: [],
            PostMigration: [],
            Assembly: typeof(EfModuleDescriptorTests).Assembly);

        Assert.Null(descriptor.ProviderContext("SqlServer"));

        var failure = Assert.Throws<NotSupportedException>(() => descriptor.RequireProviderContext("SqlServer"));
        Assert.Contains("Acme.Widgets", failure.Message, StringComparison.Ordinal);
        Assert.Contains("sqlserver", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Discover_refuses_a_case_insensitive_name_collision_naming_both_sources()
    {
        var first = BuildModuleAssembly("EfModuleDescriptorTests.Collision.First", "Acme.Widgets");
        var second = BuildModuleAssembly("EfModuleDescriptorTests.Collision.Second", "acme.widgets");

        var failure = Assert.Throws<InvalidOperationException>(() => EfModuleCatalog.Discover([first, second]));
        Assert.Contains(first.GetName().Name!, failure.Message, StringComparison.Ordinal);
        Assert.Contains(second.GetName().Name!, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_refuses_a_history_module_TableName_rejects_naming_the_assembly_and_module()
    {
        var assembly = BuildModuleAssembly("EfModuleDescriptorTests.BadHistoryModule", "Acme.Widgets", historyModule: "Bad.Name");

        var failure = Assert.Throws<InvalidOperationException>(() => EfModuleCatalog.Discover([assembly]));
        Assert.Contains(assembly.GetName().Name!, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Acme.Widgets", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_matches_a_first_party_module_name_case_insensitively_and_returns_null_for_an_unknown_name()
    {
        var descriptors = Discover();

        var lower = EfModuleCatalog.Find(descriptors, "workflows.runtime");
        var upper = EfModuleCatalog.Find(descriptors, "WORKFLOWS.RUNTIME");

        Assert.NotNull(lower);
        Assert.Same(lower, upper);
        Assert.Equal(typeof(RuntimeDbContext), lower!.ContextType);

        Assert.Null(EfModuleCatalog.Find(descriptors, "No.Such.Module"));
    }

    /// <summary>
    /// A blank name is a malformed request, not the unknown module <see cref="EfModuleCatalog.Find"/> reports
    /// as <c>null</c>, so it fails fast instead of falling through to "not found".
    /// </summary>
    [Fact]
    public void Find_refuses_a_blank_name_rather_than_reporting_it_as_unknown()
    {
        var descriptors = Discover();

        Assert.Throws<ArgumentException>(() => EfModuleCatalog.Find(descriptors, ""));
        Assert.Throws<ArgumentException>(() => EfModuleCatalog.Find(descriptors, "   "));
    }

    /// <summary>Builds a minimal in-memory assembly carrying one <see cref="EfModuleAttribute"/> declaration.</summary>
    private static Assembly BuildModuleAssembly(string assemblyName, string moduleName, string? historyModule = null)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        var constructor = typeof(EfModuleAttribute).GetConstructor([typeof(string), typeof(Type)])!;
        var historyModuleProperty = typeof(EfModuleAttribute).GetProperty(nameof(EfModuleAttribute.HistoryModule))!;
        builder.SetCustomAttribute(new CustomAttributeBuilder(
            constructor,
            [moduleName, typeof(object)],
            [historyModuleProperty],
            [historyModule ?? $"Elsa{moduleName.Replace(".", "", StringComparison.Ordinal)}"]));
        return builder;
    }
}
