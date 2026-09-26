using Xunit;
using static Elsa.Architecture.Tests.NuplaneHostSettings;

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

        // Elsa.Foundation.Host shares only its three CShells.*.Abstractions contracts today, so its closure holds
        // with nothing to examine. That is stated here rather than skipped: once it shares an Elsa assembly this
        // fails, and the theory above then checks that share's closure.
        Assert.Empty(Examined(SharedAssemblies(ReadNuplane("Elsa.Foundation.Host"))));

        // The edge behind the original bug, resolved from the real csproj files.
        Assert.Contains("Elsa.Events.Core", ElsaReferences["Elsa.Serialization.Core"]);
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
