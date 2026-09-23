using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Keeps each host's <c>Nuplane:Loading:SharedAssemblies</c> and <c>Nuplane:HostProvidedPackages</c> in step
/// (issue #1951).
/// </summary>
/// <remarks>
/// <para>
/// A shared assembly always resolves to the host's own copy, whatever version a feed package was built
/// against. Nuplane refuses a feed package that needs a newer host copy — at reconciliation, with stage
/// <c>host-version-unsatisfied</c> — but only for a dependency the host <em>declares</em> it provides. A
/// shared assembly whose package is not declared is therefore the original bug in full: the feed package is
/// acquired, bound to the older host copy, and fails later at a missing member.
/// </para>
/// <para>
/// The two lists live in different sections and answer different questions (<c>docs/foundation-host-feeds.md</c>
/// says so), so nothing but this guard keeps them from drifting. It reads each host's <c>appsettings.json</c>
/// through the same JSON configuration provider the host reads it with, and relies on a shared assembly's name
/// being its package's id, as it is for every Elsa and CShells package these hosts share.
/// </para>
/// <para>
/// A shared assembly may be left undeclared only by a named entry in <see cref="Exemptions"/>, and every
/// entry says why. An exemption is a fact about what a host shares today, so one that names an assembly the
/// host no longer shares fails too, rather than lingering as permission nobody is using.
/// </para>
/// </remarks>
public sealed class HostProvidedPackagesGuardTests
{
    /// <summary>The hosts known to share assemblies; <see cref="Every_host_that_shares_assemblies_is_guarded"/> keeps this complete.</summary>
    private static readonly string[] SharingHosts = ["Elsa.Foundation.Host", "Elsa.Workbench"];

    private const string WorkbenchSourceBuildVersion =
        "A source-built Workbench records its Elsa packages as 1.0.0 while Elsa feed packages require " +
        ">= 4.0.0-preview.N, so declaring this one would refuse every Elsa feed package that depends on it. " +
        "Lifts once source builds carry real versions (ADR 0067, #1144).";

    /// <summary>
    /// Shared assemblies a host deliberately leaves undeclared, per host, each with the reason. Nothing else
    /// may be shared without a declaration.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Exemptions =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["Elsa.Workbench"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Elsa.Activities.Design.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Activities.Runtime.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Caching.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Expressions.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Expressions.JavaScript.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Http.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Locking.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Mediator.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Primitives"] = WorkbenchSourceBuildVersion,
                ["Elsa.Serialization.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Tasks.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Tasks.Schedules"] = WorkbenchSourceBuildVersion,
                ["Elsa.Workflows.Design.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Workflows.Design.Persistence.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Workflows.Runtime.Core"] = WorkbenchSourceBuildVersion
            }
        };

    public static TheoryData<string> Hosts => [.. SharingHosts];

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Every_shared_assembly_is_declared_host_provided_or_exempted_by_name(string host)
    {
        var nuplane = ReadNuplane(host);
        var shared = SharedAssemblies(nuplane);
        var exempted = ExemptionsFor(host);

        Assert.NotEmpty(shared);
        Assert.Empty(Undeclared(shared, HostProvidedPackages(nuplane), exempted));
        Assert.Empty(StaleExemptions(shared, exempted));
    }

    /// <summary>
    /// A host added later that shares assemblies must be added to <see cref="SharingHosts"/>, or the theory above
    /// would stay green while never reading it. An exemption for a host the theory does not read is refused
    /// for the same reason.
    /// </summary>
    [Fact]
    public void Every_host_that_shares_assemblies_is_guarded()
    {
        var sharing = Directory.EnumerateDirectories(Path.Join(RepoRoot, "src", "apps"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(host => File.Exists(Path.Join(RepoRoot, "src", "apps", host, "appsettings.json")))
            .Where(host => SharedAssemblies(ReadNuplane(host)).Count > 0)
            .Order(StringComparer.Ordinal);

        Assert.Equal(SharingHosts.Order(StringComparer.Ordinal), sharing);
        Assert.All(Exemptions.Keys, host => Assert.Contains(host, SharingHosts));
    }

    /// <summary>An exemption has to say why, and the only reason on record today is tied to #1144.</summary>
    [Fact]
    public void Every_exemption_names_its_reason()
    {
        Assert.All(
            Exemptions.Values.SelectMany(exempted => exempted.Values),
            reason => Assert.Contains("#1144", reason, StringComparison.Ordinal));
    }

    /// <summary>The detector itself, so the theory's green means "all declared" rather than "nothing checked".</summary>
    [Fact]
    public void A_shared_assembly_whose_package_is_not_declared_is_flagged()
    {
        var nuplane = Fixture(
            shared: ["CShells.Abstractions", "Elsa.Primitives", "Acme.Contracts", "Elsa"],
            declared: ["cshells.abstractions", "Elsa."]);

        Assert.Equal(
            ["Acme.Contracts", "Elsa"],
            Undeclared(SharedAssemblies(nuplane), HostProvidedPackages(nuplane), NoExemptions));
    }

    /// <summary>
    /// An exemption covers exactly the assembly it names: a second undeclared share beside an exempted one is
    /// still flagged, so an exemption list cannot turn into a blanket pass for a host.
    /// </summary>
    [Fact]
    public void An_exempted_share_does_not_mask_a_different_undeclared_one()
    {
        var nuplane = Fixture(
            shared: ["CShells.Abstractions", "Elsa.Primitives", "Elsa.Caching.Core"],
            declared: ["CShells.Abstractions"]);
        var exempted = Exempt("Elsa.Primitives");

        Assert.Equal(
            ["Elsa.Caching.Core"],
            Undeclared(SharedAssemblies(nuplane), HostProvidedPackages(nuplane), exempted));
    }

    /// <summary>
    /// An exemption for an assembly the host no longer shares is reported, so removing a share without
    /// removing its exemption fails instead of leaving permission behind for a share that could come back
    /// undeclared.
    /// </summary>
    [Fact]
    public void An_exemption_for_an_assembly_the_host_no_longer_shares_is_flagged()
    {
        var nuplane = Fixture(shared: ["CShells.Abstractions"], declared: ["CShells.Abstractions"]);
        var exempted = Exempt("Elsa.Primitives");

        Assert.Equal(["Elsa.Primitives"], StaleExemptions(SharedAssemblies(nuplane), exempted));
    }

    /// <summary>
    /// Every shared name that neither a declaration nor a named exemption covers. A declaration ending in
    /// <c>.</c> is a prefix, anything else an exact id, both compared case-insensitively — the matching
    /// Nuplane itself applies to package ids. An exemption is always an exact name.
    /// </summary>
    private static IReadOnlyList<string> Undeclared(
        IReadOnlyList<string> shared,
        IReadOnlyList<string> declared,
        IReadOnlyDictionary<string, string> exempted) =>
    [
        .. shared
            .Where(name => !exempted.ContainsKey(name))
            .Where(name => !declared.Any(entry => entry.EndsWith('.')
                ? name.StartsWith(entry, StringComparison.OrdinalIgnoreCase)
                : string.Equals(name, entry, StringComparison.OrdinalIgnoreCase)))
    ];

    /// <summary>Every exemption that names an assembly the host does not share.</summary>
    private static IReadOnlyList<string> StaleExemptions(IReadOnlyList<string> shared, IReadOnlyDictionary<string, string> exempted) =>
        [.. exempted.Keys.Where(name => !shared.Contains(name, StringComparer.OrdinalIgnoreCase))];

    private static IReadOnlyDictionary<string, string> NoExemptions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> ExemptionsFor(string host) =>
        Exemptions.GetValueOrDefault(host) ?? NoExemptions;

    private static IReadOnlyDictionary<string, string> Exempt(string name) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = "Fixture exemption (#1144)." };

    /// <summary>A <c>Nuplane</c> section shaped like a host's, built in memory.</summary>
    private static IConfiguration Fixture(string[] shared, string[] declared) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                shared.Select((name, index) => KeyValuePair.Create($"Loading:SharedAssemblies:{index}:Name", (string?)name))
                    .Concat(declared.Select((entry, index) => KeyValuePair.Create($"HostProvidedPackages:{index}", (string?)entry))))
            .Build();

    private static IReadOnlyList<string> SharedAssemblies(IConfiguration nuplane) =>
    [
        .. nuplane.GetSection("Loading:SharedAssemblies").GetChildren()
            .Select(entry => entry["Name"])
            .OfType<string>()
    ];

    private static IReadOnlyList<string> HostProvidedPackages(IConfiguration nuplane) =>
        [.. nuplane.GetSection("HostProvidedPackages").GetChildren().Select(entry => entry.Value).OfType<string>()];

    private static IConfigurationSection ReadNuplane(string host) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Join(RepoRoot, "src", "apps", host, "appsettings.json"))
            .Build()
            .GetSection("Nuplane");

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
