using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// T076: architecture guards for the design-persistence provider boundary introduced by Spec 093 US4.
/// <para>
/// Two independent guarantees are pinned here:
/// </para>
/// <list type="number">
/// <item>
/// The provider-neutral design contract cores (for example
/// <c>Elsa.Workflows.Design.Persistence.Core</c> and <c>Elsa.Activities.Design.Persistence.Core</c>)
/// have no direct or transitive edge to any Groundwork provider project or package — asserted at both
/// the declared csproj reference-graph level and the restored (compiled) <c>project.assets.json</c>
/// level, mirroring <see cref="EfCoreSurfaceScanner"/>.
/// </item>
/// <item>
/// Every one of the four Groundwork providers composes the complete design surface — the shared design
/// store families, both design lane bindings and their direct v2 storage-unit declarations, the design
/// atomic writer, and the schema readiness guard — so a new provider cannot be added without design
/// registration and a design store cannot be added without every provider picking it up. This is
/// asserted from the registration sources by enumeration rather than a hardcoded roster wherever
/// feasible.
/// </item>
/// </list>
/// This suite deliberately does <b>not</b> tighten the EF surface ratchet to zero; the design EF projects
/// still exist on this branch and are removed by T072/T073, after which T075 drives the ratchet to zero.
/// The negative-dependency assertions here target the core-to-Groundwork edge, which is already absent.
/// </summary>
public sealed class DesignPersistenceBoundaryTests
{
    // ---------------------------------------------------------------------------------------------
    // Part 1 — core-to-Groundwork negative dependency
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The design contract cores the catalog treats as provider-neutral. Discovery is by convention
    /// (a <c>*.Design*.Core</c> project outside a provider folder); these two must always be present so
    /// a broken glob cannot silently turn the guard into a vacuous pass.
    /// </summary>
    private static readonly string[] RequiredDesignContractCores =
    [
        "Elsa.Workflows.Design.Persistence.Core",
        "Elsa.Activities.Design.Persistence.Core"
    ];

    [Fact]
    public void Design_contract_cores_are_discovered_including_the_two_persistence_cores()
    {
        var cores = DiscoverDesignContractCores(LoadGraph());
        var names = cores.Select(core => Path.GetFileNameWithoutExtension(core.FullPath)).ToArray();

        Assert.All(
            RequiredDesignContractCores,
            required => Assert.Contains(required, names));
        // Every discovered core lives outside a concrete provider folder.
        Assert.All(cores, core => Assert.False(IsProviderScopedPath(core.RelativePath)));
    }

    [Fact]
    public void Design_contract_core_reference_graphs_do_not_reach_groundwork()
    {
        var graph = LoadGraph();
        var cores = DiscoverDesignContractCores(graph);

        var violations = cores
            .SelectMany(core => FindGroundworkReach(graph, core, includeResolved: false))
            .Sorted();

        Assert.True(
            violations.Count == 0,
            "A provider-neutral design contract core reached a Groundwork provider through its csproj " +
            "reference graph:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Design_contract_core_resolved_packages_do_not_include_groundwork()
    {
        var graph = LoadGraph();
        var cores = DiscoverDesignContractCores(graph);

        // The compiled (restored) level is only meaningful once the reviewed cores are restored; a
        // silent skip would hide a resolved Groundwork edge, so require the two named cores' assets.
        var unrestored = cores
            .Where(core => RequiredDesignContractCores.Contains(Path.GetFileNameWithoutExtension(core.FullPath)))
            .Where(core => !core.HasAssets)
            .Select(core => core.RelativePath)
            .Sorted();
        Assert.True(
            unrestored.Count == 0,
            "Run 'dotnet restore Elsa.Server.slnx' before this gate; the resolved-package check cannot " +
            "verify absence of a Groundwork edge for an unrestored core: " + string.Join(", ", unrestored));

        var violations = cores
            .Where(core => core.HasAssets)
            .SelectMany(core => core.ResolvedPackages
                .Where(IsGroundworkPackage)
                .Select(package => $"{core.RelativePath} resolves Groundwork package {package}"))
            .Sorted();

        Assert.True(
            violations.Count == 0,
            "A provider-neutral design contract core resolved a Groundwork package at restore time:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Negative control: proves the reach detector actually fires. A synthetic core that references a
    /// Groundwork project, declares a Groundwork package, and resolves one must produce all three
    /// violation kinds — otherwise the passing real-tree assertions above would be toothless.
    /// </summary>
    [Fact]
    public void Groundwork_reach_detection_flags_a_synthetic_violating_core()
    {
        var groundwork = new ProjectNode(
            "/repo/src/Elsa/Persistence/Groundwork/Elsa.Persistence.Groundwork.csproj",
            "src/Elsa/Persistence/Groundwork/Elsa.Persistence.Groundwork.csproj",
            ProjectReferences: [],
            PackageReferences: ["Groundwork.Documents"],
            ResolvedPackages: [],
            HasAssets: true);
        var core = new ProjectNode(
            "/repo/src/Fake/Design/Persistence/Core/Fake.Design.Persistence.Core.csproj",
            "src/Fake/Design/Persistence/Core/Fake.Design.Persistence.Core.csproj",
            ProjectReferences: [groundwork.FullPath],
            PackageReferences: ["Groundwork.Documents"],
            ResolvedPackages: ["Groundwork.Documents", "Microsoft.Extensions.DependencyInjection"],
            HasAssets: true);
        var graph = new Dictionary<string, ProjectNode>(StringComparer.Ordinal)
        {
            [groundwork.FullPath] = groundwork,
            [core.FullPath] = core
        };

        var violations = FindGroundworkReach(graph, core, includeResolved: true).Sorted();

        Assert.Contains(violations, v => v.Contains("Groundwork project", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("references Groundwork package", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("resolves Groundwork package", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("src/Elsa/Persistence/Groundwork/Elsa.Persistence.Groundwork.csproj", true)]
    [InlineData("src/Elsa/Workflows/Design/Persistence/Groundwork/Elsa.Workflows.Design.Persistence.Groundwork.csproj", true)]
    [InlineData("src/Elsa/Workflows/Design/Persistence/Core/Elsa.Workflows.Design.Persistence.Core.csproj", false)]
    [InlineData("src/Elsa/Persistence/Core/Elsa.Persistence.Core.csproj", false)]
    public void Groundwork_project_classification_is_correct(string relativePath, bool expected) =>
        Assert.Equal(expected, IsGroundworkProjectPath(relativePath));

    [Theory]
    [InlineData("Groundwork.Documents", true)]
    [InlineData("Groundwork", true)]
    [InlineData("Microsoft.Extensions.DependencyInjection", false)]
    [InlineData("Elsa.Persistence.Core", false)]
    public void Groundwork_package_classification_is_correct(string package, bool expected) =>
        Assert.Equal(expected, IsGroundworkPackage(package));

    // ---------------------------------------------------------------------------------------------
    // Part 2 — complete provider-registration coverage
    // ---------------------------------------------------------------------------------------------

    /// <summary>The four Groundwork providers that must each compose the full design surface.</summary>
    private static readonly IReadOnlySet<string> ExpectedProviders =
        new HashSet<string>(["Sqlite", "SqlServer", "PostgreSql", "MongoDb"], StringComparer.Ordinal);

    /// <summary>
    /// The design registration methods and the tokens each must contain: the lane binding that lets a
    /// cross-lane caller resolve its target, the direct v2 storage-unit declarations, and the design
    /// atomic writer.
    /// </summary>
    private static readonly (string RelativePath, string[] RequiredTokens)[] DesignRegistrationSurfaces =
    [
        (
            "src/Elsa/Workflows/Design/Persistence/Groundwork/DependencyInjection/GroundworkWorkflowsDesignStoreRegistration.cs",
            [
                "AddGroundworkStorageLane<WorkflowsDesignGroundworkStorageManifestSource>",
                "AddGroundworkStorageUnit(",
                "IDesignAtomicWriter, GroundworkDesignAtomicWrite"
            ]),
        (
            "src/Elsa/Activities/Design/Persistence/Groundwork/DependencyInjection/GroundworkActivitiesDesignStoreRegistration.cs",
            [
                "AddGroundworkStorageLane<ActivitiesDesignGroundworkStorageManifestSource>",
                "AddGroundworkStorageUnit(",
                "IDesignAtomicWriter, GroundworkDesignAtomicWrite"
            ])
    ];

    /// <summary>
    /// Each direct-v2 lane: its lane type, the storage manifest whose units it publishes, and the
    /// registration that binds the lane and declares those units.
    /// </summary>
    private static readonly (string Lane, string Manifest, string Registration)[] DesignLanes =
    [
        (
            "src/Elsa/Workflows/Design/Persistence/Groundwork/WorkflowsDesignGroundworkStorageManifestSource.cs",
            "WorkflowsDesignStorageManifest",
            "src/Elsa/Workflows/Design/Persistence/Groundwork/DependencyInjection/GroundworkWorkflowsDesignStoreRegistration.cs"),
        (
            "src/Elsa/Activities/Design/Persistence/Groundwork/ActivitiesDesignGroundworkStorageManifestSource.cs",
            "ActivitiesDesignStorageManifest",
            "src/Elsa/Activities/Design/Persistence/Groundwork/DependencyInjection/GroundworkActivitiesDesignStoreRegistration.cs"),
        (
            "src/Elsa/Workflows/Publishing/Persistence/Groundwork/PublishingGroundworkStorageManifestSource.cs",
            "PublishingGroundworkStorageManifest",
            "src/Elsa/Workflows/Publishing/Persistence/Groundwork/DependencyInjection/GroundworkPublishingStoreRegistration.cs")
    ];

    private const string StoreFamiliesRegistration =
        "src/Elsa/Persistence/Groundwork/ReferenceComposition/GroundworkUnifiedStoreFamiliesRegistration.cs";

    [Fact]
    public void Exactly_the_four_expected_providers_expose_a_unified_registration()
    {
        var providers = DiscoverProviderRegistrations().Keys.ToHashSet(StringComparer.Ordinal);

        Assert.True(
            providers.SetEquals(ExpectedProviders),
            "The set of Groundwork provider unified registrations drifted from the four design-covered " +
            $"providers. Expected [{string.Join(", ", ExpectedProviders.Order())}], found " +
            $"[{string.Join(", ", providers.Order())}]. A new provider must compose the design families " +
            "(and this roster updated); a removed provider must drop out of the design contract.");
    }

    [Fact]
    public void Every_provider_registration_composes_the_design_families_and_a_provider_connection()
    {
        var violations = new List<string>();
        foreach (var (provider, path) in DiscoverProviderRegistrations().OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var source = ReadSource(path);
            // Design store set + both design manifest sources arrive through the shared families call.
            if (!source.Contains("AddGroundworkUnifiedStoreFamilies(", StringComparison.Ordinal))
                violations.Add($"{provider}: does not call AddGroundworkUnifiedStoreFamilies (design store set + manifest sources).");
            // Every lane in that set declares v2 units, and the session source admits them at startup against
            // the target's connection. A preset that composes the lanes but no connection fails at boot.
            if (!source.Contains("AddGroundworkStorageProviderConnection(", StringComparison.Ordinal))
                violations.Add($"{provider}: does not open a Groundwork v2 provider connection.");
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    // The readiness guard was the v1 document store's: each provider leaf wired a Start-phase validator over
    // its own admitted schema. Under v2 the storage session source admits every declared unit itself, so the
    // guard has no separate owner to assert and the per-provider registration it hung off no longer exists.

    [Fact]
    public void The_shared_store_families_registration_composes_both_design_lanes()
    {
        var source = ReadSource(FullPath(StoreFamiliesRegistration));

        Assert.Contains("AddGroundworkWorkflowsDesignStores(", source, StringComparison.Ordinal);
        Assert.Contains("AddGroundworkActivitiesDesignStores(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_design_registration_registers_its_manifest_sources_and_atomic_writer()
    {
        var violations = new List<string>();
        foreach (var (path, tokens) in DesignRegistrationSurfaces)
        {
            var source = ReadSource(FullPath(path));
            violations.AddRange(tokens
                .Where(token => !source.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{path}: missing registration token '{token}'."));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Each_design_registration_pairs_every_replaced_contract_with_a_groundwork_implementation()
    {
        var violations = new List<string>();
        foreach (var (path, _) in DesignRegistrationSurfaces)
        {
            var source = ReadSource(FullPath(path));
            foreach (var orphan in FindOrphanReplacements(source))
                violations.Add($"{path}: RemoveAll<{orphan}> has no matching AddScoped<{orphan}, …> (incomplete design store replacement).");
        }

        Assert.True(
            violations.Count == 0,
            "A design store contract is replaced without a Groundwork implementation registered for it:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// A design lane declares its storage units directly against the public v2 catalog, so it must be
    /// identity-only: it carries a feature identity a cross-lane caller can resolve a target from, and it
    /// publishes the same manifest its registration declares units from. It must contribute no composed
    /// host manifest — implementing the manifest-source seam would pull the lane back into the v1
    /// document-store closure, and would put it in a deployment schema that no longer describes it.
    /// </summary>
    [Fact]
    public void Every_design_lane_is_identity_only_and_publishes_the_manifest_its_registration_declares()
    {
        var violations = new List<string>();
        foreach (var (lanePath, manifest, registrationPath) in DesignLanes)
        {
            var lane = ReadSource(FullPath(lanePath));
            var registration = ReadSource(FullPath(registrationPath));
            var laneType = Path.GetFileNameWithoutExtension(lanePath);

            if (!lane.Contains(": IGroundworkStorageLane", StringComparison.Ordinal))
                violations.Add($"{lanePath}: does not implement IGroundworkStorageLane, so its target cannot be resolved.");
            if (lane.Contains("IGroundworkStorageManifestSource", StringComparison.Ordinal) ||
                lane.Contains("CreateDeclarationAsync", StringComparison.Ordinal))
            {
                violations.Add(
                    $"{lanePath}: contributes a composed host manifest. A direct-v2 lane is identity-only.");
            }

            if (!lane.Contains($"{manifest}.CreateUnits()", StringComparison.Ordinal))
                violations.Add($"{lanePath}: does not publish {manifest}.CreateUnits().");
            if (!registration.Contains($"AddGroundworkStorageLane<{laneType}>", StringComparison.Ordinal))
                violations.Add($"{registrationPath}: does not bind lane '{laneType}' to a target.");
            if (!registration.Contains($"{manifest}.CreateUnits()", StringComparison.Ordinal))
                violations.Add($"{registrationPath}: does not declare units from {manifest}.");
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Negative control: the orphan-replacement parser must flag a <c>RemoveAll&lt;T&gt;</c> with no
    /// matching <c>AddScoped&lt;T&gt;</c>, and must accept a correctly paired replacement.
    /// </summary>
    [Fact]
    public void Orphan_replacement_detection_flags_an_unpaired_removal()
    {
        const string paired =
            "services.RemoveAll<IWorkflowDefinitionStore>();" +
            "services.AddScoped<IWorkflowDefinitionStore, GroundworkWorkflowDefinitionStore>();";
        const string orphaned =
            "services.RemoveAll<IWorkflowDefinitionStore>();" +
            "services.RemoveAll<IOrphanStore>();" +
            "services.AddScoped<IWorkflowDefinitionStore, GroundworkWorkflowDefinitionStore>();";

        Assert.Empty(FindOrphanReplacements(paired));
        Assert.Equal(["IOrphanStore"], FindOrphanReplacements(orphaned));
    }

    /// <summary>
    /// Negative control: the provider-set assertion must reject both an added and a missing provider,
    /// proving the roster comparison in
    /// <see cref="Exactly_the_four_expected_providers_expose_a_unified_registration"/> has teeth.
    /// </summary>
    [Fact]
    public void Provider_set_comparison_rejects_drift()
    {
        var extra = new HashSet<string>(ExpectedProviders, StringComparer.Ordinal) { "Cassandra" };
        var missing = ExpectedProviders.Where(p => p != "MongoDb").ToHashSet(StringComparer.Ordinal);

        Assert.False(extra.SetEquals(ExpectedProviders));
        Assert.False(missing.SetEquals(ExpectedProviders));
        Assert.True(ExpectedProviders.SetEquals(new HashSet<string>(ExpectedProviders, StringComparer.Ordinal)));
    }

    // ---------------------------------------------------------------------------------------------
    // Source-scan helpers
    // ---------------------------------------------------------------------------------------------

    private static readonly Regex RemoveAllPattern =
        new(@"RemoveAll<\s*(?<type>[A-Za-z0-9_.]+)\s*>", RegexOptions.Compiled);

    private static readonly Regex AddScopedPattern =
        new(@"AddScoped<\s*(?<type>[A-Za-z0-9_.]+)\s*(?:,[^>]*)?>", RegexOptions.Compiled);

    /// <summary>Every contract removed by a replacement that lacks a matching scoped implementation.</summary>
    private static IReadOnlyList<string> FindOrphanReplacements(string source)
    {
        var added = AddScopedPattern.Matches(source).Select(match => match.Groups["type"].Value).ToHashSet(StringComparer.Ordinal);
        return RemoveAllPattern.Matches(source)
            .Select(match => match.Groups["type"].Value)
            .Where(type => !added.Contains(type))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static readonly Regex ProviderRegistrationPattern =
        new(@"Groundwork(?<provider>Sqlite|SqlServer|PostgreSql|MongoDb)UnifiedRegistration\.cs$", RegexOptions.Compiled);

    private static Dictionary<string, string> DiscoverProviderRegistrations()
    {
        var root = Path.Combine(RepoRoot, "src", "Elsa", "Persistence", "Groundwork");
        return Directory.EnumerateFiles(root, "Groundwork*UnifiedRegistration.cs", SearchOption.AllDirectories)
            .Where(path => path.Replace('\\', '/').Contains("/Unified/DependencyInjection/", StringComparison.Ordinal))
            .Select(path => (path, match: ProviderRegistrationPattern.Match(path.Replace('\\', '/'))))
            .Where(entry => entry.match.Success)
            .ToDictionary(entry => entry.match.Groups["provider"].Value, entry => entry.path, StringComparer.Ordinal);
    }

    private static string ReadSource(string fullPath) => File.ReadAllText(fullPath);

    // ---------------------------------------------------------------------------------------------
    // Groundwork classification
    // ---------------------------------------------------------------------------------------------

    private static bool IsGroundworkProjectPath(string relativePath) =>
        relativePath.Replace('\\', '/').Contains("/Groundwork/", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileNameWithoutExtension(relativePath).Contains(".Groundwork", StringComparison.OrdinalIgnoreCase);

    private static bool IsGroundworkPackage(string package) =>
        package.Equals("Groundwork", StringComparison.OrdinalIgnoreCase) ||
        package.StartsWith("Groundwork.", StringComparison.OrdinalIgnoreCase);

    private static bool IsProviderScopedPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.Contains("/Groundwork/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/EFCore/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/EntityFrameworkCore/", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex DesignContractCorePattern =
        new(@"\.Design(\.[A-Za-z0-9]+)*\.Core\.csproj$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static IReadOnlyList<ProjectNode> DiscoverDesignContractCores(IReadOnlyDictionary<string, ProjectNode> graph) =>
        graph.Values
            .Where(node => DesignContractCorePattern.IsMatch(Path.GetFileName(node.FullPath)))
            .Where(node => !IsProviderScopedPath(node.RelativePath))
            .OrderBy(node => node.RelativePath, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Every Groundwork edge reachable from <paramref name="root"/>: a transitively referenced Groundwork
    /// project, a declared (transitive) Groundwork package, and — when <paramref name="includeResolved"/>
    /// is set and assets exist — a resolved Groundwork package.
    /// </summary>
    private static IEnumerable<string> FindGroundworkReach(
        IReadOnlyDictionary<string, ProjectNode> graph,
        ProjectNode root,
        bool includeResolved)
    {
        var reachable = Reachable(graph, root);

        foreach (var path in reachable.Where(path => IsGroundworkProjectPath(graph[path].RelativePath)))
            yield return $"{root.RelativePath} references Groundwork project {graph[path].RelativePath}";

        var packages = reachable.Append(root.FullPath)
            .SelectMany(path => graph[path].PackageReferences)
            .Where(IsGroundworkPackage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
            yield return $"{root.RelativePath} references Groundwork package {package}";

        if (!includeResolved || !root.HasAssets)
            yield break;

        foreach (var package in root.ResolvedPackages.Where(IsGroundworkPackage).Distinct(StringComparer.OrdinalIgnoreCase))
            yield return $"{root.RelativePath} resolves Groundwork package {package}";
    }

    private static HashSet<string> Reachable(IReadOnlyDictionary<string, ProjectNode> graph, ProjectNode root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(root.ProjectReferences.Where(graph.ContainsKey));
        while (pending.TryPop(out var current))
        {
            if (!result.Add(current))
                continue;
            foreach (var reference in graph[current].ProjectReferences.Where(graph.ContainsKey))
                pending.Push(reference);
        }
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Project graph
    // ---------------------------------------------------------------------------------------------

    private static IReadOnlyDictionary<string, ProjectNode> LoadGraph()
    {
        var srcRoot = Path.Combine(RepoRoot, "src");
        var nodes = Directory.EnumerateFiles(srcRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path))
            .Select(LoadNode)
            .ToArray();
        return nodes.ToDictionary(node => node.FullPath, StringComparer.Ordinal);
    }

    private static ProjectNode LoadNode(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var document = XDocument.Load(fullPath);
        var directory = Path.GetDirectoryName(fullPath)!;

        var projectReferences = document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(reference => Path.GetFullPath(Path.Combine(
                directory,
                reference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var packageReferences = document.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var assetsPath = Path.Combine(directory, "obj", "project.assets.json");
        var hasAssets = File.Exists(assetsPath);
        var resolvedPackages = hasAssets ? ReadResolvedPackages(assetsPath) : [];

        return new ProjectNode(
            fullPath,
            Path.GetRelativePath(RepoRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/'),
            projectReferences,
            packageReferences,
            resolvedPackages,
            hasAssets);
    }

    private static string[] ReadResolvedPackages(string assetsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (!document.RootElement.TryGetProperty("libraries", out var libraries))
            return [];

        return libraries.EnumerateObject()
            .Where(library => !library.Value.TryGetProperty("type", out var type) ||
                              !string.Equals(type.GetString(), "project", StringComparison.OrdinalIgnoreCase))
            .Select(library => library.Name.Split('/', 2)[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsIgnored(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FullPath(string relativePath) =>
        Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
            }

            throw new DirectoryNotFoundException("Could not find the repository root (Elsa.Server.slnx).");
        }
    }

    private sealed record ProjectNode(
        string FullPath,
        string RelativePath,
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> PackageReferences,
        IReadOnlyList<string> ResolvedPackages,
        bool HasAssets);
}
