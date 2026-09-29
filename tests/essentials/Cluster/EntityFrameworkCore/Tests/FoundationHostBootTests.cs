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
/// configured, the host is a cluster of one on the in-process default. The same host is also upgraded in place, a release
/// of the fixture dropped into its feed folder while it runs, which is where the previous release staying loaded in the
/// load context Nuplane gave it, which it never unloads, shows (spec 183, FR-021, amended 2026-09-29). And #2150's: a
/// package that carries its own copies of the assemblies the host shares binds the host's, because Nuplane binds,
/// validates and matches those shipped shares itself.
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

    /// <summary>
    /// An upgrade waits for the feed folder's debounce, a reconcile, the shell reload and the old generation's drain, which
    /// CShells bounds at 30 seconds, before the version can finalize.
    /// </summary>
    private static readonly TimeSpan UpgradePatience = TimeSpan.FromSeconds(90);

    /// <summary>The header the host's module-management API reads its key from, restated: the host is never loaded into this process.</summary>
    private const string ModuleManagementKeyHeader = "X-Elsa-Module-Management-Key";

    private const string ModuleManagementKey = "foundation-host-boot-tests";

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

        await StartAsync(feed.CarryingDirectory, overrides: [(ShareSetting("Elsa.Cluster.Core", "MajorVersion"), "0")]);

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

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(overrides:
        [
            ($"{added}:Name", FoundationHostFeed.FixturePackageId),
            ($"{added}:MajorVersion", FixtureMajor.ToString())
        ]));

        // Only what names the refusal is asserted: Nuplane's exception, the package and the assembly.
        Assert.Contains("NuplaneStartupReconciliationException", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"'{FoundationHostFeed.FixturePackageId}'", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Spec 183's FR-021, amended 2026-09-29: an EF module upgraded in place on a running host finalizes the version only
    /// its new release reads, and its feature serves that version, with no restart. Nuplane never unloads the load context
    /// it gave the previous release, so a report that kept reading every loaded declaration would intersect the two
    /// releases for the life of the process, [1] ∩ [1, 2], and the record would stay at 1 and the feature dormant for ever
    /// - a host that looks healthy in every other way.
    /// </summary>
    [Fact]
    public async Task An_ef_module_upgraded_in_place_finalizes_the_version_only_its_new_release_reads_without_a_restart()
    {
        await SeedAsync(ConnectionString);
        await StartAsync(feed.PreviousDirectory);
        // The previous release reads version 1 alone, so nothing past it is finalized, and its feature serves at 1.
        Assert.Equal(("1", HttpStatusCode.OK), ((await RecordAsync(ConnectionString)).FinalizedVersion, (await Orders()).Status));

        _host!.UpgradeInPlace(FoundationHostFeed.FixturePackageId, feed.FixturePackage);

        await WaitUntilAsync(async () => (await RecordAsync(ConnectionString)).FinalizedVersion == "2", UpgradePatience);
        await WaitUntilAsync(async () => await Orders() is (HttpStatusCode.OK, var text) && text.Contains("is at 2", StringComparison.Ordinal), UpgradePatience);
        Assert.True(_host.IsRunning, "The host must be upgraded in place, not restarted.");
    }

    /// <summary>
    /// An in-place upgrade to a release with a migration this database has not applied, on a host that validates its
    /// migrations: the reload onto it is refused with the migration pending, and the running generation keeps serving, so
    /// nothing past version 1, the one it reads, is finalized. Once the migration is applied out of process, the same
    /// release activates on the next reload - which is what shows the refusal was the pending migration and nothing else -
    /// and the previous release, drained, stops counting, so version 2 finalizes.
    /// </summary>
    [Fact]
    public async Task An_in_place_upgrade_whose_migration_is_not_applied_is_refused_under_validate_until_it_is()
    {
        await SeedAsync(ConnectionString);
        await StartAsync(feed.PreviousDirectory, moduleManagement: true);

        _host!.UpgradeInPlace(FoundationHostFeed.FixturePackageId, feed.NextPackage);

        // The reload the feed triggers is refused: the previous release keeps serving at 1, and nothing is finalized.
        await WaitUntilAsync(() => Task.FromResult(_host.Output.Contains("Reloading shell 'default' after a Nuplane reconcile was refused", StringComparison.Ordinal)), UpgradePatience);
        Assert.Contains(NextReleaseMigration, _host.Output, StringComparison.Ordinal);
        var (status, text) = await Orders();
        Assert.True(status == HttpStatusCode.OK && text.Contains("is at 1", StringComparison.Ordinal),
            $"The previous release must keep serving, but the orders feature answered {status}: {text}. Host output:{Environment.NewLine}{_host.Output}");
        Assert.Equal("1", (await RecordAsync(ConnectionString)).FinalizedVersion);

        await ApplyNextReleaseMigrationAsync(ConnectionString);
        var (reloaded, body) = await _host.PostAsync("/_module-management/reload", new Dictionary<string, string> { [ModuleManagementKeyHeader] = ModuleManagementKey });
        Assert.True(reloaded == HttpStatusCode.OK, $"Reload answered {reloaded}: {body}");

        await WaitUntilAsync(async () => (await RecordAsync(ConnectionString)).FinalizedVersion == "2", UpgradePatience);
        await WaitUntilAsync(async () => await Orders() is (HttpStatusCode.OK, var served) && served.Contains("is at 2", StringComparison.Ordinal), UpgradePatience);
    }

    /// <summary>
    /// Spec 183's FR-021, amended 2026-09-29, with eager activation turned off: an upgrade that lands before any shell was
    /// ever built finds CShells' feature catalog not yet initialized, so the previous release keeps counting until the
    /// first request builds the shell. That build reads what Nuplane lists by then, so it composes the upgrade, which
    /// serves and finalizes its version on that first request, with no restart.
    /// </summary>
    [Fact]
    public async Task With_eager_activation_off_an_upgrade_before_the_first_request_serves_and_finalizes_on_that_request()
    {
        await SeedAsync(ConnectionString);
        await StartAsync(feed.PreviousDirectory, eagerActivation: false);
        await WaitUntilAsync(() => Task.FromResult(_host!.Output.Contains(Loaded(feed.PreviousPackage), StringComparison.Ordinal)));

        _host!.UpgradeInPlace(FoundationHostFeed.FixturePackageId, feed.FixturePackage);

        await WaitUntilAsync(() => Task.FromResult(_host.Output.Contains(Loaded(feed.FixturePackage), StringComparison.Ordinal)), UpgradePatience);
        // No shell has been built, so no gate has run and nothing is finalized yet.
        Assert.Equal("1", (await RecordAsync(ConnectionString)).FinalizedVersion);

        await WaitUntilAsync(async () => await Orders() is (HttpStatusCode.OK, var text) && text.Contains("is at 2", StringComparison.Ordinal), UpgradePatience);
        await WaitUntilAsync(async () => (await RecordAsync(ConnectionString)).FinalizedVersion == "2", UpgradePatience);
        Assert.True(_host.IsRunning, "The host must be upgraded in place, not restarted.");
    }

    private async Task StartAsync(string? packages = null, bool moduleManagement = false, bool eagerActivation = true, IEnumerable<(string Key, string Value)>? overrides = null)
    {
        var settings = Settings(feed);
        settings["Elsa:Boot:EagerShellActivation:Enabled"] = eagerActivation.ToString();
        settings["Elsa:ModuleManagement:Enabled"] = moduleManagement.ToString();
        settings["Elsa:ModuleManagement:ApiKey"] = ModuleManagementKey;
        foreach (var (key, value) in overrides ?? [])
            settings[key] = value;

        _host = await FoundationHostProcess.StartAsync(
            Shells(ConnectionString, EntityFrameworkCoreFeature, OrdersFeature), packages ?? feed.Directory, settings, awaitShells: eagerActivation);
    }

    /// <summary>What the host logs once Nuplane has loaded <paramref name="package"/>, a release of the fixture.</summary>
    private static string Loaded(string package) =>
        $"Loaded package {FoundationHostFeed.FixturePackageId}@{Path.GetFileNameWithoutExtension(package)[(FoundationHostFeed.FixturePackageId.Length + 1)..]}";

    private Task<(HttpStatusCode Status, string Text)> Orders() => OrdersAsync(_host!);

    /// <summary>Every copy of <paramref name="share"/> Nuplane extracted with the fixture's package.</summary>
    private string[] Carried(string share) =>
    [
        .. Directory.EnumerateFiles(_host!.PackageInstallRoot, $"{share}.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains(FoundationHostFeed.FixturePackageId, StringComparison.OrdinalIgnoreCase))
    ];

    /// <summary>The setting that overrides <paramref name="property"/> of the host's share of <paramref name="name"/>.</summary>
    private static string ShareSetting(string name, string property) =>
        $"{SharesSection}:{SharedAssemblies(FoundationHost).Entries.ToList().FindIndex(entry => entry.Name == name)}:{property}";

    private Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? patience = null) =>
        Polling.UntilAsync(condition, patience ?? Patience, TimeSpan.FromMilliseconds(100), () => $"Host output:{Environment.NewLine}{_host!.Output}");
}
