using System.Net;
using Elsa.Cluster.Core.Contracts;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2143's acceptance, on the real thing: the built <c>Elsa.Foundation.Host</c> as a child process, with the feed-module
/// fixture packed into a directory feed, and Nuplane loading it as it loads any package. Nothing here composes the host,
/// its membership or the module's load context: what <see cref="FeedLoadedEfModuleTests"/> assembles in process, the host
/// assembles itself, from its own shipped <c>appsettings.json</c> shares and its own <c>Program.cs</c>. With no membership
/// configured, the host is a cluster of one on the in-process default. And #2150's: a package that carries its own copies
/// of the assemblies the host shares binds the host's, because Nuplane binds, validates and matches those shipped shares
/// itself.
/// </summary>
/// <remarks>
/// The host carries EF Core, the four engines and <c>Elsa.Persistence.EntityFramework</c> for its opt-in cluster membership
/// (#2151), and the module binds those copies. Its feeds still offer EF Core and the Sqlite engine's closure, taken by
/// <see cref="FoundationHostFeed"/> from the package cache that restoring this project filled, so the run needs no network
/// and a host that acquired a second copy could; <see cref="FoundationHostClusterBootTests"/> asserts it does not.
/// </remarks>
[Collection(FoundationHostCollection.Name)]
public sealed class FoundationHostBootTests(FoundationHostFeed feed) : IAsyncLifetime
{
    private const string SharesSection = "Nuplane:Loading:SharedAssemblies";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-boot-{Guid.NewGuid():N}.db");
    private FoundationHostProcess? _host;

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Stops the host before its database is deleted, and deletes it even if stopping the host failed.</summary>
    public async Task DisposeAsync()
    {
        try
        {
            if (_host is not null)
                await _host.DisposeAsync();
        }
        finally
        {
            DeleteDatabaseFiles(_file);
        }
    }

    /// <summary>
    /// The module finalizes its new version at activation as a single host (spec 181, FR-021) and its feature serves at
    /// once (spec 182, FR-019): the record's finalized version moves from 1 to 2 with no operator and no restart.
    /// </summary>
    [Fact]
    public async Task A_feed_loaded_ef_module_finalizes_its_new_version_at_activation_and_its_feature_serves()
    {
        await SeedAsync(ConnectionString);

        await StartAsync();

        await WaitUntilAsync(async () => (await RecordAsync(ConnectionString)).FinalizedVersion == "2");
        Assert.Equal(HttpStatusCode.OK, (await Orders()).Status);
    }

    /// <summary>
    /// While a hold is on the version the feature needs, the host keeps it dormant and says why; releasing the hold on the
    /// database leaves dormancy in the running host, with no restart and no reload.
    /// </summary>
    [Fact]
    public async Task A_feed_loaded_feature_stays_dormant_while_its_version_is_held_and_serves_once_the_hold_is_released()
    {
        await SeedAsync(ConnectionString, hold: "canary of 2");

        await StartAsync();

        var (status, body) = await Orders();
        Assert.Equal((HttpStatusCode.Conflict, "1"), (status, (await RecordAsync(ConnectionString)).FinalizedVersion));
        Assert.Contains("held", body, StringComparison.OrdinalIgnoreCase);

        await ReleaseHoldAsync(ConnectionString);

        await WaitUntilAsync(async () => (await Orders()).Status == HttpStatusCode.OK);
        Assert.Equal("2", (await RecordAsync(ConnectionString)).FinalizedVersion);
    }

    /// <summary>
    /// #2150: the fixture's package carries its own copies of <c>Elsa.Persistence.Schema</c> and <c>Elsa.Cluster.Core</c>,
    /// and Nuplane, which loads every assembly of a host-integrated graph unless the host's shared-assembly policy matches
    /// it, takes both for the host's shares on the name, token and major the host declares. So the module binds the host's
    /// copies: it finalizes at activation and its feature serves, exactly as when it carries none.
    /// </summary>
    [Fact]
    public async Task A_feed_package_carrying_its_own_copies_of_shared_assemblies_binds_the_hosts_and_finalizes()
    {
        await SeedAsync(ConnectionString);

        await StartAsync(feed.CarryingDirectory);

        Assert.All(FoundationHostFeed.CarriedShares, share => Assert.Single(Carried(share)));
        await WaitUntilAsync(async () => (await RecordAsync(ConnectionString)).FinalizedVersion == "2");
        Assert.Equal(HttpStatusCode.OK, (await Orders()).Status);
    }

    /// <summary>
    /// The same package on a host whose <c>Elsa.Cluster.Core</c> share declares major 0, as every Elsa share did before
    /// #2150: Nuplane's matcher does not take it, the package's own copy is the one the module binds, and nothing fails at
    /// startup. The host starts and the module admits, but the feature's contract is a different type from the one the host
    /// registered its dormancy check under, so the feature cannot ask it and fails. The share, not the copy, decides. (The
    /// <c>Elsa.Persistence.Schema</c> entry cannot show the same: the module's persistence is the host's, and reaches the
    /// host's schema types whichever copy of them the package carries.)
    /// </summary>
    [Fact]
    public async Task A_carried_copy_whose_share_declares_another_major_is_the_one_the_module_binds_and_its_feature_cannot_reach_the_hosts_dormancy_check()
    {
        await SeedAsync(ConnectionString);

        await StartAsync(feed.CarryingDirectory, (ShareSetting("Elsa.Cluster.Core", "MajorVersion"), "0"));

        // Wait for the failure itself rather than for time to pass: the request either reaches the host's check or it does not.
        var answered = default((HttpStatusCode Status, string Text));
        await Polling.UntilAsync(
            async () => (answered = await Orders()).Status == HttpStatusCode.InternalServerError,
            Patience,
            TimeSpan.FromMilliseconds(100),
            () => $"The orders endpoint last answered {answered.Status}: {answered.Text}{Environment.NewLine}Host output:{Environment.NewLine}{_host!.Output}");
        Assert.Contains(nameof(ISchemaDormancyCheck), answered.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A share the host has no copy of refuses the package that carries it, and the host with it: Nuplane will not load
    /// the package's copy of an assembly its policy leaves to the host, so the host stops at its startup reconciliation
    /// and names the package, the assembly and the entry, rather than serving without the module. The entry here is for
    /// the fixture's own assembly, which no host carries whatever else it references.
    /// </summary>
    [Fact]
    public async Task A_share_the_host_has_no_copy_of_refuses_the_package_that_carries_it_and_the_host_does_not_start()
    {
        await SeedAsync(ConnectionString);
        var added = $"{SharesSection}:{SharedAssemblies(FoundationHost).Entries.Count}";

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(
            ($"{added}:Name", FoundationHostFeed.FixturePackage),
            ($"{added}:MajorVersion", FixtureMajor.ToString())));

        // Only what names the refusal is asserted: Nuplane's exception, the package and the assembly.
        Assert.Contains("NuplaneStartupReconciliationException", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"'{FoundationHostFeed.FixturePackage}'", refusal.Message, StringComparison.Ordinal);
    }

    private Task StartAsync(params (string Key, string Value)[] overrides) => StartAsync(feed.Directory, overrides);

    private async Task StartAsync(string packages, params (string Key, string Value)[] overrides)
    {
        var settings = Settings(feed);
        foreach (var (key, value) in overrides)
            settings[key] = value;

        _host = await FoundationHostProcess.StartAsync(Shells(ConnectionString, EntityFrameworkCoreFeature, OrdersFeature), packages, settings);
    }

    private Task<(HttpStatusCode Status, string Text)> Orders() => OrdersAsync(_host!);

    /// <summary>Every copy of <paramref name="share"/> Nuplane extracted with the fixture's package.</summary>
    private string[] Carried(string share) =>
    [
        .. Directory.EnumerateFiles(_host!.PackageInstallRoot, $"{share}.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains(FoundationHostFeed.FixturePackage, StringComparison.OrdinalIgnoreCase))
    ];

    /// <summary>The setting that overrides <paramref name="property"/> of the host's share of <paramref name="name"/>.</summary>
    private static string ShareSetting(string name, string property) =>
        $"{SharesSection}:{SharedAssemblies(FoundationHost).Entries.ToList().FindIndex(entry => entry.Name == name)}:{property}";

    private Task WaitUntilAsync(Func<Task<bool>> condition) =>
        Polling.UntilAsync(condition, Patience, TimeSpan.FromMilliseconds(100), () => $"Host output:{Environment.NewLine}{_host!.Output}");
}
