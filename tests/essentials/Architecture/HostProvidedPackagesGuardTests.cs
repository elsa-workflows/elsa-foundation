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
/// </remarks>
public sealed class HostProvidedPackagesGuardTests
{
    /// <summary>The hosts known to share assemblies; <see cref="Every_host_that_shares_assemblies_is_guarded"/> keeps this complete.</summary>
    private static readonly string[] SharingHosts = ["Elsa.Foundation.Host", "Elsa.Workbench"];

    public static TheoryData<string> Hosts => [.. SharingHosts];

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Every_shared_assembly_is_declared_host_provided(string host)
    {
        var nuplane = ReadNuplane(host);
        var shared = SharedAssemblies(nuplane);

        Assert.NotEmpty(shared);
        Assert.Empty(Undeclared(shared, HostProvidedPackages(nuplane)));
    }

    /// <summary>
    /// A host added later that shares assemblies must be added to <see cref="SharingHosts"/>, or the theory above
    /// would stay green while never reading it.
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
    }

    /// <summary>The detector itself, so the theory's green means "all declared" rather than "nothing checked".</summary>
    [Fact]
    public void A_shared_assembly_whose_package_is_not_declared_is_flagged()
    {
        var nuplane = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Loading:SharedAssemblies:0:Name"] = "CShells.Abstractions",
                ["Loading:SharedAssemblies:1:Name"] = "Elsa.Primitives",
                ["Loading:SharedAssemblies:2:Name"] = "Acme.Contracts",
                ["Loading:SharedAssemblies:3:Name"] = "Elsa",
                ["HostProvidedPackages:0"] = "cshells.abstractions",
                ["HostProvidedPackages:1"] = "Elsa."
            })
            .Build();

        Assert.Equal(
            ["Acme.Contracts", "Elsa"],
            Undeclared(SharedAssemblies(nuplane), HostProvidedPackages(nuplane)));
    }

    /// <summary>
    /// Every shared name no entry covers. An entry ending in <c>.</c> is a prefix, anything else an exact id,
    /// both compared case-insensitively — the matching Nuplane itself applies to package ids.
    /// </summary>
    private static IReadOnlyList<string> Undeclared(IReadOnlyList<string> shared, IReadOnlyList<string> declared) =>
    [
        .. shared.Where(name => !declared.Any(entry => entry.EndsWith('.')
            ? name.StartsWith(entry, StringComparison.OrdinalIgnoreCase)
            : string.Equals(name, entry, StringComparison.OrdinalIgnoreCase)))
    ];

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
