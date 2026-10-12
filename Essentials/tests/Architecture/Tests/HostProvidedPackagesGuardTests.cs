using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xunit;
using static Elsa.Architecture.Tests.NuplaneHostSettings;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Keeps each host's <c>Nuplane:Loading:SharedAssemblies</c> and <c>Nuplane:HostProvidedPackages</c> in step
/// (issue #1951), in every configuration the host runs with: a source build's, and a build's with computed package
/// versions, which is how CI builds the Workbench image (#2084).
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
/// A host runs with its <c>appsettings.json</c> and its environment's <c>appsettings.{Environment}.json</c>, so every
/// configuration is read with that overlay layered over <c>appsettings.json</c>, as the host reads it: an overlay's
/// list replaces the base list entry by entry, not whole. A source build ships the committed overlays. A build with
/// computed versions ships the ones under <see cref="ComputedVersionsDirectory"/> in their place, and
/// <see cref="A_computed_build_ships_its_computed_overlays_and_a_source_build_its_committed_ones"/> proves the swap.
/// </para>
/// <para>
/// A shared assembly may be left undeclared only by a source build, and only by a named entry in
/// <see cref="Exemptions"/>, and every entry says why. An exemption is a fact about what a source build shares and
/// leaves undeclared today, so one that names an assembly the host no longer shares, or one the source build
/// declares after all, fails too, rather than lingering as permission nobody is using. A build with computed
/// versions records each Elsa package at the version the feed carries, which is what the exemptions wait for, so it
/// has no exemptions: everything it shares, it declares.
/// </para>
/// </remarks>
public sealed class HostProvidedPackagesGuardTests
{
    /// <summary>The hosts known to share assemblies; <see cref="Every_host_that_shares_assemblies_is_guarded"/> keeps this complete.</summary>
    private static readonly string[] SharingHosts = ["Elsa.Foundation.Host", "Elsa.Workbench"];

    private const string WorkbenchSourceBuildVersion =
        "A source-built Workbench records its Elsa packages at their line's dev version (x.y.0-dev), which every Elsa feed " +
        "package's range excludes, so declaring this one would refuse every Elsa feed package that depends on it. A build " +
        "with computed versions, the CI image among them, declares it (ADR 0067, #1144, #2084).";

    private const string FoundationHostSourceBuildVersion =
        "A source-built Elsa.Foundation.Host records its Elsa packages at their line's dev version (x.y.0-dev), which " +
        "every Elsa feed package's range excludes, so declaring this one would refuse every Elsa feed package that " +
        "depends on it. A build with computed versions, the CI image among them, declares it (ADR 0067, #1144, #2126).";

    /// <summary>
    /// Shared assemblies a host's source build deliberately leaves undeclared, per host, each with the reason. Nothing
    /// else may be shared without a declaration, and nothing at all by a build with computed versions.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Exemptions =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["Elsa.Workbench"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Elsa.Activities.Design.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Activities.Runtime.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Attention.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Caching.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Cluster.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Events.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Expressions.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Expressions.JavaScript.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Http.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Locking.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Mediator.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Persistence.Schema"] = WorkbenchSourceBuildVersion,
                ["Elsa.Pipelines.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Primitives"] = WorkbenchSourceBuildVersion,
                ["Elsa.Serialization.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Tasks.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Tasks.Schedules"] = WorkbenchSourceBuildVersion,
                ["Elsa.Workflows.Design.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Workflows.Design.Persistence.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Workflows.Design.Validations.Core"] = WorkbenchSourceBuildVersion,
                ["Elsa.Workflows.Runtime.Core"] = WorkbenchSourceBuildVersion
            },
            ["Elsa.Foundation.Host"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Elsa.Attention.Core"] = FoundationHostSourceBuildVersion,
                ["Elsa.Caching.Core"] = FoundationHostSourceBuildVersion,
                ["Elsa.Cluster.Core"] = FoundationHostSourceBuildVersion,
                ["Elsa.Events.Core"] = FoundationHostSourceBuildVersion,
                ["Elsa.Expressions.Core"] = FoundationHostSourceBuildVersion,
                ["Elsa.Locking.Core"] = FoundationHostSourceBuildVersion,
                ["Elsa.Mediator.Core"] = FoundationHostSourceBuildVersion,
                ["Elsa.Persistence.EntityFramework"] = FoundationHostSourceBuildVersion,
                ["Elsa.Persistence.Schema"] = FoundationHostSourceBuildVersion,
                ["Elsa.Pipelines.Core"] = FoundationHostSourceBuildVersion,
                ["Elsa.Primitives"] = FoundationHostSourceBuildVersion,
                ["Elsa.Serialization.Core"] = FoundationHostSourceBuildVersion,
                ["Elsa.Tasks.Core"] = FoundationHostSourceBuildVersion
            }
        };

    /// <summary>The <c>Nuplane</c> keys a computed overlay may add to the committed one: the declaration and its comment.</summary>
    private static readonly string[] DeclarationKeys = ["Nuplane:HostProvidedPackages", "Nuplane:// HostProvidedPackages"];

    /// <summary>
    /// Each host with no overlay, as it runs in an environment it ships no <c>appsettings.{Environment}.json</c> for, and
    /// with each committed overlay.
    /// </summary>
    public static TheoryData<string, string?> SourceBuildConfigurations => Configurations(host => [null, .. Overlays(host, directory: null)]);

    /// <summary>Each host with each overlay a build with computed versions ships in place of the committed one.</summary>
    public static TheoryData<string, string?> ComputedBuildConfigurations => Configurations(host => [.. ComputedOverlays(host)]);

    [Theory]
    [MemberData(nameof(SourceBuildConfigurations))]
    public void Every_shared_assembly_a_source_build_runs_with_is_declared_or_exempted_by_name(string host, string? overlay)
    {
        var nuplane = ReadNuplane(host, overlay);
        var shared = SharedAssemblies(nuplane);
        var declared = HostProvidedPackages(nuplane);
        var exempted = ExemptionsFor(host);

        Assert.NotEmpty(shared);
        Assert.Empty(Undeclared(shared, declared, exempted));
        Assert.Empty(StaleExemptions(shared, declared, exempted));
    }

    /// <summary>
    /// The configuration the CI image runs with, among any other a build with computed versions ships. Every Elsa package
    /// such a build carries is at the version the feed carries, so a share it leaves undeclared is unchecked for no reason,
    /// and no exemption applies.
    /// </summary>
    [Theory]
    [MemberData(nameof(ComputedBuildConfigurations))]
    public void Every_shared_assembly_a_computed_build_runs_with_is_declared(string host, string? overlay)
    {
        var nuplane = ReadNuplane(host, overlay);
        var shared = SharedAssemblies(nuplane);

        Assert.NotEmpty(shared);
        Assert.Empty(Undeclared(shared, HostProvidedPackages(nuplane), NoExemptions));
    }

    /// <summary>
    /// An exemption is scoped to source builds, so a host that has one must ship the production configuration its computed
    /// build declares the exempted shares in; without it the theory above has nothing to read for that host, and the
    /// exemption would hold for the CI image too.
    /// </summary>
    [Fact]
    public void Every_host_with_an_exemption_declares_its_shares_in_a_computed_production_configuration()
    {
        Assert.NotEmpty(Exemptions);
        Assert.All(
            Exemptions.Keys,
            host => Assert.Contains($"{ComputedVersionsDirectory}/appsettings.Production.json", ComputedOverlays(host)));
    }

    /// <summary>
    /// A computed overlay is its committed namesake plus the declaration, and nothing else: the settings a CI image runs
    /// with may not drift from the ones a source build runs with, and a computed overlay cannot change what the host shares.
    /// </summary>
    [Fact]
    public void A_computed_overlay_is_the_committed_one_plus_the_declaration()
    {
        var overlays = SharingHosts.SelectMany(host => ComputedOverlays(host).Select(overlay => (Host: host, Overlay: overlay))).ToArray();

        Assert.NotEmpty(overlays);
        Assert.All(overlays, entry =>
        {
            var committed = CommittedNamesake(entry.Overlay);
            Assert.True(
                File.Exists(HostFile(entry.Host, committed)),
                $"{entry.Host} ships {entry.Overlay} for computed builds, but no {committed} for source builds to be its namesake.");
            Assert.Equal(SettingsBesideDeclaration(entry.Host, committed), SettingsBesideDeclaration(entry.Host, entry.Overlay));
        });
    }

    /// <summary>
    /// The swap the two theories rely on, both ways, as MSBuild evaluates the host's project: with the calculator's
    /// pack-properties file, which is what sets <c>ElsaVersionComputationCommit</c>, each computed overlay is the one copied
    /// to the output under the committed file's name; without it, the committed file is, and no computed overlay is copied
    /// at all. Both are copied always, so neither survives the other kind of build in the same output directory.
    /// </summary>
    [Fact]
    public void A_computed_build_ships_its_computed_overlays_and_a_source_build_its_committed_ones()
    {
        var hosts = SharingHosts.Where(host => ComputedOverlays(host).Any()).ToArray();

        Assert.NotEmpty(hosts);
        Assert.All(hosts, host =>
        {
            var source = ContentByTargetPath(host, computed: false);
            var computed = ContentByTargetPath(host, computed: true);

            Assert.DoesNotContain(source.Values, item => item.FullPath.Contains($"{ComputedVersionsDirectory}{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            Assert.All(ComputedOverlays(host), overlay =>
            {
                var committed = CommittedNamesake(overlay);
                Assert.Equal(new ContentItem(HostFile(host, committed), "Always", "Always"), source[committed]);
                Assert.Equal(new ContentItem(HostFile(host, overlay), "Always", "Always"), computed[committed]);
            });
        });
    }

    /// <summary>
    /// A host added later that shares assemblies must be added to <see cref="SharingHosts"/>, or the theories above
    /// would stay green while never reading it. An exemption for a host they do not read is refused for the same reason.
    /// </summary>
    [Fact]
    public void Every_host_that_shares_assemblies_is_guarded()
    {
        Assert.Equal(SharingHosts.Order(StringComparer.Ordinal), HostsThatShareAssemblies());
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

    /// <summary>The detector itself, so the theories' green means "all declared" rather than "nothing checked".</summary>
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

        Assert.Equal(["Elsa.Primitives"], StaleExemptions(SharedAssemblies(nuplane), HostProvidedPackages(nuplane), exempted));
    }

    /// <summary>
    /// An exemption for a share the source build declares anyway is reported too: the declaration makes a source build,
    /// whose Elsa packages are at the dev version, refuse every feed package that depends on that share. Either the
    /// declaration goes, or the exemption does, knowingly.
    /// </summary>
    [Fact]
    public void An_exemption_for_a_share_the_source_build_declares_is_flagged()
    {
        var nuplane = Fixture(shared: ["CShells.Abstractions", "Elsa.Primitives"], declared: ["CShells.Abstractions", "elsa.primitives"]);
        var exempted = Exempt("Elsa.Primitives");

        Assert.Equal(["Elsa.Primitives"], StaleExemptions(SharedAssemblies(nuplane), HostProvidedPackages(nuplane), exempted));
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
            .Where(name => !IsDeclared(name, declared))
    ];

    /// <summary>Every exemption that names an assembly the host does not share, or one the configuration declares after all.</summary>
    private static IReadOnlyList<string> StaleExemptions(
        IReadOnlyList<string> shared,
        IReadOnlyList<string> declared,
        IReadOnlyDictionary<string, string> exempted) =>
        [.. exempted.Keys.Where(name => !shared.Contains(name, StringComparer.OrdinalIgnoreCase) || IsDeclared(name, declared))];

    private static bool IsDeclared(string name, IReadOnlyList<string> declared) =>
        declared.Any(entry => entry.EndsWith('.')
            ? name.StartsWith(entry, StringComparison.OrdinalIgnoreCase)
            : string.Equals(name, entry, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, string> NoExemptions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> ExemptionsFor(string host) =>
        Exemptions.GetValueOrDefault(host) ?? NoExemptions;

    private static IReadOnlyDictionary<string, string> Exempt(string name) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = "Fixture exemption (#1144)." };

    private static TheoryData<string, string?> Configurations(Func<string, IEnumerable<string?>> overlays)
    {
        var data = new TheoryData<string, string?>();
        foreach (var host in SharingHosts)
        foreach (var overlay in overlays(host))
            data.Add(host, overlay);

        return data;
    }

    /// <summary>The <c>appsettings.{Environment}.json</c> files in the host's directory or a directory under it, <c>/</c>-separated relative to the host's.</summary>
    private static IEnumerable<string> Overlays(string host, string? directory)
    {
        var path = directory is null ? HostDirectory(host) : HostFile(host, directory);
        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "appsettings.*.json")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Select(name => directory is null ? name : $"{directory}/{name}")
                .Order(StringComparer.Ordinal)
            : [];
    }

    private static IEnumerable<string> ComputedOverlays(string host) => Overlays(host, ComputedVersionsDirectory);

    /// <summary>The committed overlay a computed one replaces: the same file name, in the host's directory.</summary>
    private static string CommittedNamesake(string computedOverlay) => computedOverlay[(ComputedVersionsDirectory.Length + 1)..];

    /// <summary>Every setting <paramref name="overlay"/> holds by itself, except the declaration and its comment.</summary>
    private static IReadOnlyDictionary<string, string> SettingsBesideDeclaration(string host, string overlay) =>
        new ConfigurationBuilder()
            .AddJsonFile(HostFile(host, overlay))
            .Build()
            .AsEnumerable()
            .Where(setting => setting.Value is not null)
            .Where(setting => !DeclarationKeys.Any(key =>
                setting.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                setting.Key.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(setting => setting.Key, setting => setting.Value!, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The host project's <c>Content</c> items as MSBuild evaluates them, by the path each is copied to — its <c>Link</c>,
    /// else its own — for a source build, or for a build importing a pack-properties file as the calculator writes it.
    /// </summary>
    private static IReadOnlyDictionary<string, ContentItem> ContentByTargetPath(string host, bool computed)
    {
        var scratch = Directory.CreateTempSubdirectory(nameof(HostProvidedPackagesGuardTests));
        try
        {
            var properties = Path.Join(scratch.FullName, "package-versions.props");
            File.WriteAllText(properties,
                "<Project><PropertyGroup><ElsaVersionComputationCommit>0123456789abcdef0123456789abcdef01234567</ElsaVersionComputationCommit></PropertyGroup></Project>");
            var result = Path.Join(scratch.FullName, "items.json");

            var (exitCode, output) = ChildProcess.Dotnet(
            [
                "msbuild", HostFile(host, $"{host}.csproj"), "-getItem:Content", $"-getResultOutputFile:{result}", "-nologo",
                .. computed ? [$"-p:CustomBeforeDirectoryBuildProps={properties}"] : Array.Empty<string>()
            ]);
            Assert.True(exitCode == 0, $"evaluating {host} failed with exit {exitCode}:\n{output}");

            using var items = JsonDocument.Parse(File.ReadAllText(result));
            return items.RootElement.GetProperty("Items").GetProperty("Content").EnumerateArray()
                .ToDictionary(
                    item => Metadata(item, "Link") is { Length: > 0 } link ? link : Metadata(item, "Identity"),
                    item => new ContentItem(
                        Metadata(item, "FullPath"),
                        Metadata(item, "CopyToOutputDirectory"),
                        Metadata(item, "CopyToPublishDirectory")),
                    StringComparer.Ordinal);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private static string Metadata(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>A <c>Content</c> item: the file it copies, and when it copies it to the output and publish directories.</summary>
    private sealed record ContentItem(string FullPath, string CopyToOutputDirectory, string CopyToPublishDirectory);

    /// <summary>A <c>Nuplane</c> section shaped like a host's, built in memory.</summary>
    private static IConfiguration Fixture(string[] shared, string[] declared) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                shared.Select((name, index) => KeyValuePair.Create($"Loading:SharedAssemblies:{index}:Name", (string?)name))
                    .Concat(declared.Select((entry, index) => KeyValuePair.Create($"HostProvidedPackages:{index}", (string?)entry))))
            .Build();

    private static IReadOnlyList<string> HostProvidedPackages(IConfiguration nuplane) =>
        [.. nuplane.GetSection("HostProvidedPackages").GetChildren().Select(entry => entry.Value).OfType<string>()];
}
