using System.Net;
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
/// load context Nuplane gave it, which it never unloads, shows (spec 183, FR-021, amended 2026-09-29).
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

    private async Task StartAsync(string? packages = null, bool moduleManagement = false, bool eagerActivation = true)
    {
        var settings = Settings(feed);
        settings["Elsa:Boot:EagerShellActivation:Enabled"] = eagerActivation.ToString();
        settings["Elsa:ModuleManagement:Enabled"] = moduleManagement.ToString();
        settings["Elsa:ModuleManagement:ApiKey"] = ModuleManagementKey;
        _host = await FoundationHostProcess.StartAsync(
            Shells(ConnectionString, EntityFrameworkCoreFeature, OrdersFeature), packages ?? feed.Directory, settings, awaitShells: eagerActivation);
    }

    /// <summary>What the host logs once Nuplane has loaded <paramref name="package"/>, a release of the fixture.</summary>
    private static string Loaded(string package) =>
        $"Loaded package {FoundationHostFeed.FixturePackageId}@{Path.GetFileNameWithoutExtension(package)[(FoundationHostFeed.FixturePackageId.Length + 1)..]}";

    private Task<(HttpStatusCode Status, string Text)> Orders() => OrdersAsync(_host!);

    private Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? patience = null) =>
        Polling.UntilAsync(condition, patience ?? Patience, TimeSpan.FromMilliseconds(100), () => $"Host output:{Environment.NewLine}{_host!.Output}");
}
