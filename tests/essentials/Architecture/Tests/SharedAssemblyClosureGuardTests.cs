using System.Text.Json;
using Xunit;
using static Elsa.Architecture.Tests.NuplaneHostSettings;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Keeps each host's shared Elsa assemblies closed under Elsa project dependencies.
/// </summary>
/// <remarks>
/// <para>
/// A shared assembly resolves to the host's copy for every package Nuplane loads from a feed, but its
/// dependencies are not shared with it. One that is not shared too is loaded again, privately, into each
/// feed feature's load context. Every type that dependency defines then exists twice, as the host's copy the
/// shared assembly was bound to and as the feature's own, and the runtime treats the two as unrelated. The
/// first time a feature and a shared assembly exchange such a type, it fails at runtime with an invalid cast,
/// a missing method or a service that resolves to nothing. Build and load both succeed.
/// </para>
/// <para>
/// So for every shared assembly built from a project under <c>src/</c>, every Elsa project it references,
/// directly or transitively through <c>ProjectReference</c>, must be shared as well. Only Elsa-to-Elsa edges
/// count: package references and non-Elsa projects are outside this rule, and a shared assembly with no
/// project under <c>src/</c> (the <c>CShells.*</c> contracts) is not examined. The rule is about type identity,
/// not versions, so it holds whatever <see cref="HostProvidedPackagesGuardTests"/> exempts.
/// </para>
/// </remarks>
public sealed class SharedAssemblyClosureGuardTests
{
    /// <summary>Every Elsa project under <c>src/</c>, by assembly name, with the Elsa projects it references directly.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ElsaReferences { get; } = ProjectGraph.LoadElsaReferences(RepoRoot);

    /// <summary>
    /// The host-composed shares (ADR 0067, amended 2026-09-29; #2143): the contracts of what a host composes once on its
    /// own container rather than in a shell - cluster membership, its readability report and the dormancy check's source
    /// (spec 183, FR-017; ADR 0078) - which a feed-loaded package reaches only if it sees the host's types. They are on
    /// Line B, not Line A: they cross the host/package boundary for the persistence and cluster domains, not for every
    /// domain. <c>Elsa.Persistence.Schema</c> is the EF-free half of the schema-family surface; an EF module package
    /// carries its own copy of <c>Elsa.Persistence.EntityFramework</c>, and without this share its finalization gate
    /// never finds the host's fleet and its dormancy source never finds its gate.
    /// </summary>
    internal static readonly string[] HostComposedShares = ["Elsa.Cluster.Core", "Elsa.Persistence.Schema"];

    public static TheoryData<string> Hosts => [.. HostsThatShareAssemblies()];

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Shared_elsa_assemblies_are_closed_under_elsa_project_references(string host)
    {
        var gaps = UnsharedDependencies(SharedAssemblies(ReadNuplane(host)), ElsaReferences);

        Assert.True(gaps.Count == 0, Report(host, gaps));
    }

    /// <summary>
    /// The theory above passes trivially for a host that shares no Elsa assembly, so this pins what it actually
    /// examines, and that the project graph it walks is the real one.
    /// </summary>
    [Fact]
    public void The_closure_scan_examines_real_elsa_shares()
    {
        var workbench = SharedAssemblies(ReadNuplane("Elsa.Workbench"));
        Assert.NotEmpty(Examined(workbench));
        Assert.Equal(workbench.Where(ProjectGraph.IsElsa), Examined(workbench));

        // Elsa.Foundation.Host shares Line A's ten contracts (#2126), the two host-composed ones (#2143) and the
        // Elsa.Persistence.EntityFramework its cluster membership provider carries (#2151), beside its three
        // CShells.*.Abstractions ones and EF Core's own. Line A is closed under its own dependencies by definition (ADR
        // 0067), the two host-composed ones reach nothing but Line A, and Elsa.Persistence.EntityFramework reaches only
        // Elsa.Primitives and Elsa.Persistence.Schema, so the theory above holds with every one of the thirteen examined and
        // none of them reaching outside the set.
        var foundationHost = SharedAssemblies(ReadNuplane("Elsa.Foundation.Host"));
        Assert.NotEmpty(Examined(foundationHost));
        Assert.Equal(foundationHost.Where(ProjectGraph.IsElsa), Examined(foundationHost));

        // The edge behind the original bug, resolved from the real csproj files.
        Assert.Contains("Elsa.Events.Core", ElsaReferences["Elsa.Serialization.Core"]);
    }

    /// <summary>
    /// Every host shares the host-composed contracts, and carries the projects that define them, so the types a feed-loaded
    /// package resolves them to are the ones the host registered its membership under. A host that composed membership but
    /// shared neither would look healthy: every EF module package would admit, and its gate would silently never finalize.
    /// </summary>
    [Theory]
    [MemberData(nameof(Hosts))]
    public void Every_host_shares_the_contracts_of_the_membership_it_composes(string host)
    {
        var shared = SharedAssemblies(ReadNuplane(host));
        var carried = Reachable(host, ElsaReferences).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(HostComposedShares, share =>
        {
            Assert.True(shared.Contains(share, StringComparer.OrdinalIgnoreCase),
                $"{host} composes cluster membership on its own container but does not share {share} " +
                $"(src/apps/{host}/appsettings.json, Nuplane:Loading:SharedAssemblies). A feed-loaded EF module would load a " +
                "private copy of it and never reach the host's fleet (ADR 0067, amended 2026-09-29; #2143).");
            Assert.True(carried.Contains(share), $"{host} shares {share} but does not reference the project that builds it.");
        });
    }

    /// <summary>
    /// What <c>Elsa.Foundation.Host</c> carries for its EF cluster membership provider alone (ADR 0076, amended 2026-09-29;
    /// #2151). A feed-loaded EF module has to bind these as the host's copies: <c>dotnet elsa persistence</c> runs through the
    /// host's own <c>Elsa.Persistence.EntityFramework</c> and finds a module only by that assembly's attribute types, and a
    /// second EF Core would bring a second engine closure into the process.
    /// </summary>
    internal static readonly string[] FoundationHostMembershipShares =
    [
        "Elsa.Persistence.EntityFramework",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Abstractions",
        "Microsoft.EntityFrameworkCore.Relational"
    ];

    /// <summary>The public key token every EF Core assembly is signed with.</summary>
    private const string EfCorePublicKeyToken = "adb9793829ddae60";

    /// <summary>
    /// <c>Elsa.Foundation.Host</c> shares the EF closure its membership provider carries, and each strong-named entry names
    /// the token and major of the assembly the host really carries. An entry that does not is the failure that looks like
    /// success: Nuplane's matcher compares both, so a wrong one never matches, and the configured share silently does
    /// nothing once Nuplane matches shares inside a host-integrated graph.
    /// </summary>
    [Fact]
    public void Foundation_host_shares_the_ef_closure_its_membership_provider_carries()
    {
        const string host = "Elsa.Foundation.Host";
        var entries = ReadNuplane(host).GetSection("Loading:SharedAssemblies").GetChildren()
            .ToDictionary(entry => entry["Name"]!, StringComparer.OrdinalIgnoreCase);
        var carried = Reachable(host, ElsaReferences).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var locked = LockedVersions(host);

        Assert.All(FoundationHostMembershipShares, share =>
        {
            Assert.True(entries.ContainsKey(share),
                $"{host} carries {share} for its cluster membership but does not share it (src/apps/{host}/appsettings.json, " +
                "Nuplane:Loading:SharedAssemblies), so a feed-loaded EF module could bind a copy of its own (#2151).");
            Assert.True(carried.Contains(share) || locked.ContainsKey(share), $"{host} shares {share} but does not carry it.");
        });
        Assert.All(FoundationHostMembershipShares.Where(share => share.StartsWith("Microsoft.", StringComparison.Ordinal)), share =>
        {
            var entry = entries[share];
            Assert.Equal((EfCorePublicKeyToken, Major(locked[share])), (entry["PublicKeyToken"], int.Parse(entry["MajorVersion"]!)));
        });
    }

    /// <summary>The packages the host's committed lock file resolves, by id, at the version it resolves.</summary>
    private static IReadOnlyDictionary<string, string> LockedVersions(string host)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(HostFile(host, "packages.lock.json")));
        return document.RootElement.GetProperty("dependencies").EnumerateObject().Single().Value.EnumerateObject()
            .Where(package => package.Value.TryGetProperty("resolved", out _))
            .ToDictionary(package => package.Name, package => package.Value.GetProperty("resolved").GetString()!, StringComparer.OrdinalIgnoreCase);
    }

    private static int Major(string version) => int.Parse(version[..version.IndexOf('.')]);

    /// <summary>The host-composed shares reach nothing outside Line A, so sharing them adds no Line B floor of another domain.</summary>
    [Fact]
    public void The_host_composed_shares_reach_only_line_a()
    {
        var lineA = VersionLines.LineAMembers(RepoRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(HostComposedShares, share =>
            Assert.All(Reachable(share, ElsaReferences), dependency => Assert.Contains(dependency, lineA)));
    }

    /// <summary>The detector itself, so the theory's green means "closed" rather than "nothing checked".</summary>
    [Fact]
    public void A_shared_assembly_whose_elsa_dependency_is_not_shared_is_flagged()
    {
        var references = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Elsa.Serialization.Core"] = ["Elsa.Primitives", "Elsa.Events.Core"],
            ["Elsa.Mediator.Core"] = ["Elsa.Pipelines.Core"],
            ["Elsa.Events.Core"] = ["Elsa.Pipelines.Core"],
            ["Elsa.Pipelines.Core"] = [],
            ["Elsa.Primitives"] = []
        };

        Assert.Equal(
            [
                "Elsa.Events.Core, needed by Elsa.Serialization.Core",
                "Elsa.Pipelines.Core, needed by Elsa.Mediator.Core, Elsa.Serialization.Core"
            ],
            Lines(UnsharedDependencies(["CShells.Abstractions", "Elsa.Mediator.Core", "Elsa.Primitives", "Elsa.Serialization.Core"], references)));

        Assert.Empty(UnsharedDependencies(
            ["CShells.Abstractions", "Elsa.Events.Core", "Elsa.Mediator.Core", "Elsa.Pipelines.Core", "Elsa.Primitives", "Elsa.Serialization.Core"],
            references));
    }

    /// <summary>
    /// Every Elsa project a shared assembly reaches, directly or transitively, that is not itself shared, with
    /// the shared assemblies that reach it.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> UnsharedDependencies(
        IReadOnlyList<string> shared,
        IReadOnlyDictionary<string, IReadOnlyList<string>> elsaReferences)
    {
        var sharedSet = shared.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return shared
            .Where(elsaReferences.ContainsKey)
            .SelectMany(name => Reachable(name, elsaReferences)
                .Where(dependency => !sharedSet.Contains(dependency))
                .Select(dependency => (Dependency: dependency, NeededBy: name)))
            .GroupBy(edge => edge.Dependency, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                IReadOnlyList<string> (group) => [.. group.Select(edge => edge.NeededBy).Order(StringComparer.Ordinal)],
                StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Reachable(string root, IReadOnlyDictionary<string, IReadOnlyList<string>> references)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var pending = new Stack<string>(references[root]);
        while (pending.TryPop(out var name))
        {
            if (!visited.Add(name))
                continue;

            yield return name;
            foreach (var reference in references.GetValueOrDefault(name) ?? [])
                pending.Push(reference);
        }
    }

    private static IReadOnlyList<string> Examined(IReadOnlyList<string> shared) => [.. shared.Where(ElsaReferences.ContainsKey)];

    private static string Report(string host, IReadOnlyDictionary<string, IReadOnlyList<string>> gaps) =>
        $"{host} shares Elsa assemblies whose Elsa project dependencies it does not share. A feed-loaded feature " +
        "would load its own copy of each, and the types it exchanges with the shared assembly would not match the " +
        $"host's. Share each in src/apps/{host}/appsettings.json (Nuplane:Loading:SharedAssemblies):" +
        string.Concat(Lines(gaps).Select(line => $"{Environment.NewLine}  {line}"));

    private static IEnumerable<string> Lines(IReadOnlyDictionary<string, IReadOnlyList<string>> gaps) =>
        gaps.OrderBy(gap => gap.Key, StringComparer.Ordinal).Select(gap => $"{gap.Key}, needed by {string.Join(", ", gap.Value)}");
}
